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

namespace psiDeepSpeech
{
    public class DeepSpeechSubRecognizer : ConsumerProducer<(AudioBuffer,bool), String>, IDisposable
    {

        private IDeepSpeech dsclient;

        private string modelFileName = "output_graph.pbmm";

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
        public DeepSpeechSubRecognizer(Pipeline pipeline) : base(pipeline)
        {
            if (File.Exists(modelFileName))
            {
                dsclient = new DeepSpeech(modelFileName);
                dsstream = dsclient.CreateStream();

                inputStream = new BufferedAudioStream(640 * 31 * 7);

                //AudioVadIn = pipeline.CreateReceiver<(AudioBuffer, bool)>(this, ReceiveWithVad, nameof(this.AudioVadIn));

                IsBuffering = pipeline.CreateEmitter<bool>(this, nameof(this.IsBuffering));
            }
            else
            {
                throw new FileNotFoundException($"Model file {modelFileName} not found.  The model is not included in the DeepSpeech NuGet. Be sure to download the model file at https://github.com/mozilla/DeepSpeech/releases");
            }

        }

        //public Receiver<(AudioBuffer, bool)> AudioVadIn;


        public void Dispose()
        {
            if (this.dsclient != null)
            {
                dsclient.Dispose();
                dsclient = null;
            }
        }

        public Emitter<bool> IsBuffering { get; private set; }


        //protected override void Receive((AudioBuffer,double) data, Envelope e)
        //{
        //    //inputStream.Write(audio.Data, 0, audio.Length);
        //    var audio = data.Item1;
        //    var db = data.Item2;
        //    var waveBuffer = new WaveBuffer(audio.Data);
        //    if (db > 40)
        //    {
        //        if (!buffering)
        //        {
        //            buffering = true;
        //            dsstream = dsclient.CreateStream();
        //            for (int i = 0; i < 5; i++)
        //            {
        //                if (oldBuffers[i] != null)
        //                {
        //                    dsclient.FeedAudioContent(dsstream, oldBuffers[i], Convert.ToUInt32(oldBuffers[i].Length));
        //                }
        //            }
        //        }
        //        lowCount = 0;
        //        dsclient.FeedAudioContent(dsstream, waveBuffer.ShortBuffer, Convert.ToUInt32(waveBuffer.MaxSize / 2));
                
        //    }
        //    else
        //    {
        //        if (buffering)
        //        {
        //            lowCount += 1;
        //            if (lowCount > 5)
        //            {
        //                Console.Write('*');
        //                dsclient.FeedAudioContent(dsstream, waveBuffer.ShortBuffer, Convert.ToUInt32(waveBuffer.MaxSize / 2));
        //                var text = dsclient.FinishStream(dsstream);
        //                if (text.Length > 0)
        //                    Out.Post(text, e.OriginatingTime);
        //                //dsstream = dsclient.CreateStream();
        //                buffering = false;
        //            }
        //        }
        //        else
        //        {
                    
        //        }
        //    }

        //    for (int i = 0; i < 4; i++)
        //    {
        //        //Array.Copy(oldBuffers[i + 1], oldBuffers[i], oldBuffers[i + 1].Length);
        //        oldBuffers[i] = oldBuffers[i + 1];
        //    }
        //    oldBuffers[4] = new short[waveBuffer.MaxSize / 2];
        //    Array.Copy(waveBuffer.ShortBuffer, oldBuffers[4], waveBuffer.MaxSize / 2);

        //    IsBuffering.Post(buffering, e.OriginatingTime);
        //}

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
                oldBuffers[4] = new short[waveBuffer.MaxSize / 2];
                Array.Copy(waveBuffer.ShortBuffer, oldBuffers[4], waveBuffer.MaxSize / 2);
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
                if (text.Length > 0) Out.Post(text, e.OriginatingTime);
                
            }

            // Remember last audio state.
            this.lastAudioContainedSpeech = hasSpeech;

            // Update on whether audio is being processed/buffering
            IsBuffering.Post(buffering, e.OriginatingTime);
        }

        // Receiver for help signal coming in (will need to add other signals and/or modify this one
        public Receiver<bool> DoneIn;


        static void Main(string[] args)
        {
            using (Pipeline pipeline = Pipeline.Create())
            {
                var store = Store.Create(pipeline, "deepSpeechTest", ".\\recordings");
                //var storeIn = Store.Open(pipeline, "deepSpeechTest", ".\\recordings\\deepSpeechTest.0000");
                var storeIn = Store.Open(pipeline, "psiAssist", "\\linux\\repos\\psiAssistiveAgent\\Main\\data\\DataStores\\samples");
                
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
                            OutputFormat = Microsoft.Psi.Audio.WaveFormat.Create16kHz1Channel16BitPcm(),
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
