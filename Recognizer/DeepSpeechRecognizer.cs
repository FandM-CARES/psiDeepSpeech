using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Psi;
using Microsoft.Psi.Audio;
using Microsoft.Psi.Components;
using Microsoft.Psi.Speech;
using PushToTalk;

namespace psiDeepSpeech
{
    /// <summary>
    /// Recognizer that detects when speech is present and then uses DeepSpeech to recognize the speech.
    /// </summary>
    public class DeepSpeechRecognizer : Subpipeline
    {
        public DeepSpeechRecognizer(Pipeline pipeline) : this(pipeline, new DeepSpeechConfiguration())
        {
        }

        public DeepSpeechRecognizer(Pipeline pipeline, DeepSpeechConfiguration config) : base(pipeline, nameof(DeepSpeechRecognizer))
        {
            setup(pipeline, config);

            IProducer<bool> activity = null;
            IProducer<(AudioBuffer, bool)> voiceAudio = null;

            if (config.push2talk)
            {
                activity = new PushToTalkHandler(this);
                //activity.Do((x, e) => Console.WriteLine("ptt:" + x + " - " + e.OriginatingTime.Second + "." + e.OriginatingTime.Millisecond));
                
                //var repeat = Generators.Repeat<bool>(this, true, TimeSpan.FromMilliseconds(20));
                //var voiceDet = repeat.Join(ptt, Reproducible.Nearest<bool>(new RelativeTimeInterval(TimeSpan.FromSeconds(-5), TimeSpan.FromSeconds(5)))).Select(x => x.Item2);
                //voiceDet.Do((x,e) => Console.WriteLine(x + ":" + e.OriginatingTime.Second + "." + e.OriginatingTime.Millisecond));
                //this.audioIn.Out.Do((x, e) => Console.WriteLine("audio: " + e));
                
                voiceAudio = this.audioIn.Out.Join(activity, Reproducible.Nearest<bool>(new RelativeTimeInterval(TimeSpan.FromSeconds(-5), TimeSpan.FromSeconds(5))));
                //voiceAudio.Do(x => Console.Write('.'));
                voiceAudio.PipeTo(this.recognizer.In);

            }
            else
            {
                // voice activity detector
                var voiceDet = new SystemVoiceActivityDetector(this);
                activity = voiceDet;
                this.audioIn.Out.PipeTo(voiceDet);
                //this.audioIn.Do(x =>
                //{
                //    Console.Write('.');
                //});
                voiceAudio = this.audioIn.Out.Join(voiceDet.Out, Reproducible.Nearest<bool>(new RelativeTimeInterval(TimeSpan.FromSeconds(-5), TimeSpan.FromSeconds(0.5))));

                voiceAudio.PipeTo(this.recognizer.In);
            }

            if (config.storeInternalStreams)
            {
                setupStore(config.psiStoreOutName, config.psiStoreOutPath, activity, voiceAudio);
            }

        }

        private void setupStore(string name, string path, IProducer<bool> activity, IProducer<(AudioBuffer, bool)> voiceAudio)
        {
            //Microsoft.Psi.Data.Exporter storeOut = PsiStore.Create(this, "psiDeepSpeech", "testing");
            Microsoft.Psi.Data.Exporter storeOut = PsiStore.Create(this, name, path);
            storeOut.Write<AudioBuffer>(this.audioIn, "audio");
            storeOut.Write(activity, "activity");
            storeOut.Write(voiceAudio, "audioActivityJoin");
            storeOut.Write(this.TextOut, "text");
        }

        private void setup(Pipeline pipeline, DeepSpeechConfiguration config)
        {
            // Create connector
            this.audioIn = this.CreateInputConnectorFrom<AudioBuffer>(pipeline, nameof(this.AudioIn));

            // Define the outputs
            var textOut = this.CreateOutputConnectorTo<String>(pipeline, nameof(this.TextOut));
            this.TextOut = textOut.Out;

            // sub-recognizer
            this.recognizer = new DeepSpeechSubRecognizer(this, config);

            recognizer.PipeTo(textOut);

        }

        // Connector for the help signal input
        private Connector<AudioBuffer> audioIn;

        // Receiver for help signal coming in (will need to add other signals and/or modify this one
        public Receiver<AudioBuffer> AudioIn => this.audioIn.In;

        // Emitter for the text of the recognized speech
        public Emitter<String> TextOut { get; private set; }

        // SubRecognizer is where the DeepSpeechClient is, IOW it does all the work
        private DeepSpeechSubRecognizer recognizer;

        static void Main(string[] args)
        {
            using (Pipeline pipeline = Pipeline.Create())
            {
                DeepSpeechConfiguration config = new DeepSpeechConfiguration();

                // TODO: add parameters for model file, scorer file, and hotwords file
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i]){
                        case "-u":
                            Console.WriteLine("Run psiDeepSpeech.exe to do automatic speech recognition.  By default the audio is captured from the microphone.\n" +
                                "Available options are the following:\n" +
                                "    -u  Display this message\n" +
                                "    -v  Print additional debug information\n" +
                                "    -p  Enable push-to-talk (beta)\n" +
                                "    -r [name] [path] [stream]  Replay audio from PsiStore [name] at [path] with audio stream [stream]\n" +
                                "    -s [name] [path]  Store internal streams to PsiStore [name] at [path]");
                            break;
                        case "-v":
                            config.verbose = true;
                            break;
                        case "-p":
                            config.push2talk = true;
                            break;
                        case "-r":
                            config.replayFromStore = true;
                            if (args.Length < i + 4)
                            {
                                Console.WriteLine("Insufficient arguments for replay.  Use -u to see usage.");
                                return;
                            }
                            config.psiStoreInName = args[++i];
                            config.psiStoreInPath = args[++i];
                            config.psiStoreInStream = args[++i];
                            break;
                        case "-s":
                            config.storeInternalStreams = true;
                            if (args.Length < i + 3)
                            {
                                Console.WriteLine("Insufficient arguments for replay.  Use -u to see usage.");
                                return;
                            }
                            config.psiStoreOutName = args[++i];
                            config.psiStoreOutPath = args[++i];
                            break;
                        default:
                            Console.WriteLine("Unknown parameter " + args[0] + ". Use -u to see usage.");
                            return;
                    }

                }
                IProducer<AudioBuffer> audioSource;
                if (config.replayFromStore)
                {
                    // string name = args[1];   // "mmr"
                    // string path = args[2];   // "Z:\\legos\\mmr.0002"
                    // string stream = args[3]; // "AudioIn"
                    //var storeIn = PsiStore.Open(pipeline, "mmr", "Z:\\legos\\mmr.0002");
                    var storeIn = PsiStore.Open(pipeline, config.psiStoreInName, config.psiStoreInPath);
                    audioSource = storeIn.OpenStream<AudioBuffer>(config.psiStoreInStream);
                    //audioSource = null;

                    var player = new AudioPlayer(pipeline);
                    audioSource.PipeTo(player);
                }
                else
                {
                    

                    audioSource = new AudioCapture(
                        pipeline,
                        new AudioCaptureConfiguration()
                        {
                            Format = Microsoft.Psi.Audio.WaveFormat.Create16kHz1Channel16BitPcm(),
                            DropOutOfOrderPackets = true
                        }
                        );

                    //audioSource.Write("Audio", store);
                }

                DeepSpeechRecognizer dsr = new DeepSpeechRecognizer(pipeline, config); 

                audioSource.PipeTo(dsr.AudioIn);
                //audioSource.Do(x => Console.Write('x'));
                dsr.TextOut.Do(x => Console.WriteLine(x));

                Console.WriteLine("Starting...");
                pipeline.Run();
                Console.ReadLine();
            }
        }
    }
}
