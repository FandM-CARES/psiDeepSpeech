using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using Microsoft.Psi;
using Microsoft.Psi.Audio;
using Microsoft.Psi.Components;
using Microsoft.Psi.Speech;

using DeepSpeechClient;
using DeepSpeechClient.Interfaces;
using DeepSpeechClient.Models;
using NAudio.Wave;

using Newtonsoft.Json;

namespace psiDeepSpeech
{
    public class DeepSpeechSubRecognizer : ConsumerProducer<(AudioBuffer,bool), String>, IDisposable
    {

        private IDeepSpeech dsclient;

        /// <summary>
        /// Stream used to feed data into the acoustic model.
        /// </summary>
        private DeepSpeechStream dsstream;

        private BufferedAudioStream inputStream;

        private float baselineAmplitude = 0;

        private short[][] oldBuffers = new short[20][];

        private bool buffering = false;

        private int lowCount = 0;

        /// <summary>
        /// SubRecognizer that requires a signal for when speech is present.
        /// </summary>
        public DeepSpeechSubRecognizer(Pipeline pipeline) : this(pipeline, new DeepSpeechConfiguration())
        {
        }

        public DeepSpeechSubRecognizer(Pipeline pipeline, DeepSpeechConfiguration config) : base(pipeline)
        {
            string modelFileName = config.modelFileName;
            string scorerFileName = config.scorerFileName;

            if (File.Exists(modelFileName))
            {
                dsclient = new DeepSpeech(modelFileName);

                if (scorerFileName.Length > 0)
                {
                    if (File.Exists(scorerFileName))
                    {
                        dsclient.EnableExternalScorer(scorerFileName);
                    } 
                    else
                    {
                        Console.WriteLine("WARNING: Scorer file " + scorerFileName + " not found");
                    }
                }

                setBeamWidth(config.beamWidth);

                loadHotWords(config.hotwordsFileName);

                dsstream = dsclient.CreateStream();

                inputStream = new BufferedAudioStream(640 * 31 * 7);

                IsBuffering = pipeline.CreateEmitter<bool>(this, nameof(this.IsBuffering));
            }
            else
            {
                throw new FileNotFoundException($"Model file {modelFileName} not found.  The model is not included in the DeepSpeech NuGet. Be sure to download the model file at https://github.com/mozilla/DeepSpeech/releases");
            }
        }
        

        public void Dispose()
        {
            if (this.dsclient != null)
            {
                dsclient.Dispose();
                dsclient = null;
            }
        }

        public Emitter<bool> IsBuffering { get; private set; }

        private DateTime lastAudioContainingSpeechTime;
        private DateTime lastAudioOriginatingTime = DateTime.MinValue;
        private bool lastAudioContainedSpeech = false;


        protected override void Receive((AudioBuffer, bool) data, Envelope e)
        {
            byte[] audioData = data.Item1.Data;
            var waveBuffer = new WaveBuffer(audioData);

            bool hasSpeech = data.Item2;

            if (this.lastAudioOriginatingTime == DateTime.MinValue)  // default
            {
                this.lastAudioOriginatingTime = e.OriginatingTime - data.Item1.Duration;
            }

            var previousAudioOriginatingTime = this.lastAudioOriginatingTime;
            this.lastAudioOriginatingTime = e.OriginatingTime;

            if (hasSpeech)
            {
                buffering = true;

                //Console.Write('.');

                this.lastAudioContainingSpeechTime = e.OriginatingTime;

                if (!this.lastAudioContainedSpeech)
                {
                    // setup a new recognition stream
                    dsstream = dsclient.CreateStream();

                    // load in recent data
                    for (int i = 0; i < 20; i++)
                    {
                        if (oldBuffers[i] != null)
                        {
                            dsclient.FeedAudioContent(dsstream, oldBuffers[i], Convert.ToUInt32(oldBuffers[i].Length));
                        }
                    }
                }

                // feed audio to deep speech
                dsclient.FeedAudioContent(dsstream, waveBuffer.ShortBuffer, Convert.ToUInt32(waveBuffer.MaxSize / 2));
            }
            else
            {
                //Console.Write('x');
                for (int i = 0; i < 19; i++)
                {
                    //Array.Copy(oldBuffers[i + 1], oldBuffers[i], oldBuffers[i + 1].Length);
                    oldBuffers[i] = oldBuffers[i + 1];
                }
                oldBuffers[19] = new short[waveBuffer.MaxSize / 2];
                Array.Copy(waveBuffer.ShortBuffer, oldBuffers[19], waveBuffer.MaxSize / 2);
            }

            // If this is the last audio packet containing speech
            if (!hasSpeech && this.lastAudioContainedSpeech)
            {
                // done buffering
                buffering = false;

                // If this is the first audio packet containing no speech, use the time of the previous audio packet
                // as the end of the actual speech, since that is the last packet that contained any speech.
                var lastVADSpeechEndTime = this.lastAudioContainingSpeechTime;


                //Console.Write('*');
                dsclient.FeedAudioContent(dsstream, waveBuffer.ShortBuffer, Convert.ToUInt32(waveBuffer.MaxSize / 2));
                var text = dsclient.FinishStream(dsstream);
                // Debug code below
                //var metadata = dsclient.FinishStreamWithMetadata(dsstream, 3);
                //var text = "";
                //var count = 0;
                //foreach (var tran in metadata.Transcripts)
                //{
                //    text = text + count + ": ";
                //    foreach (var token in tran.Tokens)
                //    {
                //        text = text + token.Text;
                //    }
                //    text = text + "(" + tran.Confidence + ") :: ";
                //    count++;
                //}
                if (text.Length > 0) Out.Post(text, e.OriginatingTime);
                
            }

            // Remember last audio state.
            this.lastAudioContainedSpeech = hasSpeech;

            // Update on whether audio is being processed/buffering
            IsBuffering.Post(buffering, e.OriginatingTime);
        }

        // Receiver for help signal coming in (will need to add other signals and/or modify this one
        public Receiver<bool> DoneIn;


        private void setBeamWidth(uint bw)
        {
            if (bw > 0)
            {
                var beamWidth = dsclient.GetModelBeamWidth();
                Console.WriteLine("Beam width was " + beamWidth);
                dsclient.SetModelBeamWidth(bw);
                Console.WriteLine("Beam width now is " + bw);
            }
        }

        private void loadHotWords(string filename)
        {
            if (File.Exists(filename))
            {
                using (StreamReader r = new StreamReader(filename))
                {
                    string json = r.ReadToEnd();
                    Dictionary<string, float> items = JsonConvert.DeserializeObject<Dictionary<string, float>>(json);
                    foreach (var entry in items)
                    {
                        dsclient.AddHotWord(entry.Key, entry.Value);
                    }
                }
            }
        }

        static void Main(string[] args)
        {
            using (Pipeline pipeline = Pipeline.Create())
            {
                var store = PsiStore.Create(pipeline, "deepSpeechTest", ".\\recordings");
                //var storeIn = Store.Open(pipeline, "deepSpeechTest", ".\\recordings\\deepSpeechTest.0000");
                var storeIn = PsiStore.Open(pipeline, "psiAssist", "\\linux\\repos\\psiAssistiveAgent\\Main\\data\\DataStores\\samples");
                
                var replay = false;
                IProducer<AudioBuffer> audioSource;
                if (replay)
                {
                    //audioSource = storeIn.OpenStream<AudioBuffer>("Audio");
                    audioSource = storeIn.OpenStream<AudioBuffer>("AudioIn");
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

                //var waveSource = new WaveFileAudioSource(pipeline, "hello2.wav");
                //var waveSource = new WaveFileAudioSource(pipeline, "arctic_a0024.wav");
                //var audioSource = waveSource.Out;



                var bitRate = 16000 * 16 / 8;
                
                var recognizer = new DeepSpeechSubRecognizer(pipeline);



                //audioSource.Select(x => isSilentBuffer(x)).Write("Silence", store);
                //waveSource.PipeTo(recognizer);

                //var gen = Generators.Sequence(pipeline, new int[] { 0, 0, 0, 1 }, TimeSpan.FromMilliseconds(1000));
                //gen.PipeTo(recognizer.DoneIn);

                //var processor = new SimpleAcousticProcessor(pipeline);
                //audioSource.PipeTo(processor);
                //processor.DecibelsOut.Write("Decibels", store);
                //processor.AvgDecibelsOut.Write("AvgDecibels", store);
                //processor.AvgDecibelsOut.Select(x => x < 10.0).PipeTo(recognizer.DoneIn);

                var voiceDet = new SystemVoiceActivityDetector(pipeline);
                audioSource.PipeTo(voiceDet);
                //var voiceAudio = voiceDet.Out.Join(audioSource);
                var voiceAudio = audioSource.Out.Join(voiceDet.Out, Reproducible.Nearest<bool>(RelativeTimeInterval.Future()));

                voiceAudio.PipeTo(recognizer.In);
                //audioSource.Join(processor.AvgDecibelsOut).PipeTo(recognizer.In);
                //audioSource.PipeTo(recognizer);

                
                recognizer.Do(x => Console.WriteLine(x + ' '));
                recognizer.Write("Text", store);
                recognizer.IsBuffering.Write("Buffering", store);

                pipeline.Run();
            }
        }
    }
}
