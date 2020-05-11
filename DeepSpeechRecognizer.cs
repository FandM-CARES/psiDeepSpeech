using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using Microsoft.Psi;
using Microsoft.Psi.Audio;
using Microsoft.Psi.Components;
using DeepSpeechClient;
using DeepSpeechClient.Interfaces;
using DeepSpeechClient.Models;
using NAudio.Wave;

namespace psiDeepSpeech
{
    public class DeepSpeechRecognizer : ConsumerProducer<(AudioBuffer,double), String>, ISourceComponent, IDisposable
    {

        private IDeepSpeech dsclient;

        private string modelFileName = "output_graph.pbmm";

        /// <summary>
        /// Stream used to feed data into the acoustic model.
        /// </summary>
        private DeepSpeechStream dsstream;

        private BufferedAudioStream inputStream;

        private float baselineAmplitude = 0;

        private short[][] oldBuffers = new short[5][];

        private bool buffering = false;

        private int lowCount = 0;

        public DeepSpeechRecognizer(Pipeline pipeline) : base(pipeline)
        {
            if (File.Exists(modelFileName))
            {
                dsclient = new DeepSpeech(modelFileName);
                dsstream = dsclient.CreateStream();

                inputStream = new BufferedAudioStream(640 * 31 * 7);

                //DoneIn = pipeline.CreateReceiver<bool>(this, ReceiveDone, nameof(this.DoneIn));

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

        public void Start(Action<DateTime> notifyCompletionTime)
        {
            // nothing yet
        }

        public void Stop(DateTime finalOriginatingTime, Action notifyCompleted)
        {
            // nothing yet
        }

        public Emitter<bool> IsBuffering { get; private set; }


        protected override void Receive((AudioBuffer,double) data, Envelope e)
        {
            //inputStream.Write(audio.Data, 0, audio.Length);
            var audio = data.Item1;
            var db = data.Item2;
            var waveBuffer = new WaveBuffer(audio.Data);
            if (db > 40)
            {
                if (!buffering)
                {
                    buffering = true;
                    dsstream = dsclient.CreateStream();
                    for (int i = 0; i < 5; i++)
                    {
                        if (oldBuffers[i] != null)
                        {
                            dsclient.FeedAudioContent(dsstream, oldBuffers[i], Convert.ToUInt32(oldBuffers[i].Length));
                        }
                    }
                }
                lowCount = 0;
                dsclient.FeedAudioContent(dsstream, waveBuffer.ShortBuffer, Convert.ToUInt32(waveBuffer.MaxSize / 2));
                
            }
            else
            {
                if (buffering)
                {
                    lowCount += 1;
                    if (lowCount > 5)
                    {
                        Console.Write('*');
                        dsclient.FeedAudioContent(dsstream, waveBuffer.ShortBuffer, Convert.ToUInt32(waveBuffer.MaxSize / 2));
                        var text = dsclient.FinishStream(dsstream);
                        if (text.Length > 0)
                            Out.Post(text, e.OriginatingTime);
                        //dsstream = dsclient.CreateStream();
                        buffering = false;
                    }
                }
                else
                {
                    
                }
            }

            for (int i = 0; i < 4; i++)
            {
                //Array.Copy(oldBuffers[i + 1], oldBuffers[i], oldBuffers[i + 1].Length);
                oldBuffers[i] = oldBuffers[i + 1];
            }
            oldBuffers[4] = new short[waveBuffer.MaxSize / 2];
            Array.Copy(waveBuffer.ShortBuffer, oldBuffers[4], waveBuffer.MaxSize / 2);

            IsBuffering.Post(buffering, e.OriginatingTime);
        }

        // Receiver for help signal coming in (will need to add other signals and/or modify this one
        public Receiver<bool> DoneIn;

        private static bool isSilentBuffer(AudioBuffer audio)
        {
            var waveBuffer = new WaveBuffer(audio.Data);
            short[] floatBuffer = waveBuffer.ShortBuffer;
            int bufferSize = Convert.ToUInt16(waveBuffer.MaxSize / 2);
            int counter = 0;
            for (int n = 0; n < bufferSize; n++)
            {
                double dB = 20 * Math.Log10(Math.Abs(floatBuffer[n]));
                if (dB < -40)
                {
                    counter++;
                }
            }
            if (counter > bufferSize / 2)
            {
                Console.Write('*');
                return true;
            }
            else
            {
                //Console.Write('.');
                //Console.Write(counter);
                return false;
            }
        }

        static void Main(string[] args)
        {
            using (Pipeline pipeline = Pipeline.Create())
            {
                var store = Store.Create(pipeline, "deepSpeechTest", ".\\recordings");
                var storeIn = Store.Open(pipeline, "deepSpeechTest", ".\\recordings\\deepSpeechTest.0000");

                //var audioSource = new AudioCapture(
                //    pipeline,
                //    new AudioCaptureConfiguration()
                //    {
                //        OutputFormat = Microsoft.Psi.Audio.WaveFormat.Create16kHz1Channel16BitPcm(),
                //        DropOutOfOrderPackets = true
                //    }
                //    );

                //var waveSource = new WaveFileAudioSource(pipeline, "hello2.wav");
                //var waveSource = new WaveFileAudioSource(pipeline, "arctic_a0024.wav");
                //var audioSource = waveSource.Out;
                var audioSource = storeIn.OpenStream<AudioBuffer>("Audio");
                audioSource.Write("Audio", store);

                var bitRate = 16000 * 16 / 8;
                
                var recognizer = new DeepSpeechRecognizer(pipeline);



                //audioSource.Select(x => isSilentBuffer(x)).Write("Silence", store);
                //waveSource.PipeTo(recognizer);

                //var gen = Generators.Sequence(pipeline, new int[] { 0, 0, 0, 1 }, TimeSpan.FromMilliseconds(1000));
                //gen.PipeTo(recognizer.DoneIn);

                var processor = new SimpleAcousticProcessor(pipeline);
                audioSource.PipeTo(processor);
                processor.DecibelsOut.Write("Decibels", store);
                processor.AvgDecibelsOut.Write("AvgDecibels", store);
                //processor.AvgDecibelsOut.Select(x => x < 10.0).PipeTo(recognizer.DoneIn);

                audioSource.Join(processor.AvgDecibelsOut).PipeTo(recognizer.In);
                //audioSource.PipeTo(recognizer);

                
                recognizer.Do(x => Console.Write(x + ' '));
                recognizer.Write("Text", store);
                recognizer.IsBuffering.Write("Buffering", store);

                pipeline.Run();
            }
        }
    }
}
