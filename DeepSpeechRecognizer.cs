using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Psi;
using Microsoft.Psi.Audio;
using Microsoft.Psi.Components;
using Microsoft.Psi.Speech;

namespace psiDeepSpeech
{
    /// <summary>
    /// Recognizer that detects when speech is present and then uses DeepSpeech to recognize the speech.
    /// </summary>
    public class DeepSpeechRecognizer : Subpipeline
    {
        public DeepSpeechRecognizer(Pipeline pipeline) : base(pipeline, nameof(DeepSpeechRecognizer))
        {
            // Create connector
            this.audioIn = this.CreateInputConnectorFrom<AudioBuffer>(pipeline, nameof(this.AudioIn));

            // Define the outputs
            var textOut = this.CreateOutputConnectorTo<String>(pipeline, nameof(this.TextOut));
            this.TextOut = textOut.Out;

            // sub-recognizer
            var recognizer = new DeepSpeechSubRecognizer(this);

            // voice activity detector
            var voiceDet = new SystemVoiceActivityDetector(this);
            this.audioIn.Out.PipeTo(voiceDet);
            this.audioIn.Do(x =>
            {
                Console.Write('.');
            });
            var voiceAudio = this.audioIn.Out.Join(voiceDet.Out, Reproducible.Nearest<bool>(RelativeTimeInterval.Future()));

            voiceAudio.PipeTo(recognizer.In);

            recognizer.PipeTo(textOut);
        }

        // Connector for the help signal input
        private Connector<AudioBuffer> audioIn;

        // Receiver for help signal coming in (will need to add other signals and/or modify this one
        public Receiver<AudioBuffer> AudioIn => this.audioIn.In;

        // Emitter for the text of the recognized speech
        public Emitter<String> TextOut { get; private set; }

        static void Main(string[] args)
        {
            using (Pipeline pipeline = Pipeline.Create())
            {
                //var store = Store.Create(pipeline, "deepSpeechTest", ".\\recordings");
                //var storeIn = Store.Open(pipeline, "deepSpeechTest", ".\\recordings\\deepSpeechTest.0000");
                //var storeIn = Store.Open(pipeline, "psiAssist", "\\linux\\repos\\psiAssistiveAgent\\Main\\data\\DataStores\\samples");

                var replay = false;
                IProducer<AudioBuffer> audioSource;
                if (replay)
                {
                    //audioSource = storeIn.OpenStream<AudioBuffer>("AudioIn");
                    audioSource = null;
                }
                else
                {
                    audioSource = new AudioCapture(
                        pipeline,
                        new AudioCaptureConfiguration()
                        {
                            OutputFormat = Microsoft.Psi.Audio.WaveFormat.Create16kHz1Channel16BitPcm(),
                            DropOutOfOrderPackets = true
                        }
                        );

                    //audioSource.Write("Audio", store);
                }

                var dsr = new DeepSpeechRecognizer(pipeline);

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
