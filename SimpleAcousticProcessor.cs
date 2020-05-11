using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Psi;
using Microsoft.Psi.Audio;
using Microsoft.Psi.Components;
using NAudio.Wave;

namespace psiDeepSpeech
{
    public class SimpleAcousticProcessor : ConsumerProducer<AudioBuffer, AudioBuffer>
    {
        private int byteRate = 16000 * 16 / 8;

        public SimpleAcousticProcessor(Pipeline pipeline) : base(pipeline)
        {
            DecibelsOut = pipeline.CreateEmitter<double[]>(this, nameof(this.DecibelsOut));
            AvgDecibelsOut = pipeline.CreateEmitter<double>(this, nameof(this.AvgDecibelsOut));
        }

        public Emitter<double> SilenceOut { get; private set; }

        public Emitter<double[]> DecibelsOut { get; private set; }
        public Emitter<double> AvgDecibelsOut { get; private set; }

        protected override void Receive(AudioBuffer audio, Envelope e)
        {
            var waveBuffer = new WaveBuffer(audio.Data);
            short[] floatBuffer = waveBuffer.ShortBuffer;
            int bufferSize = Convert.ToUInt16(waveBuffer.MaxSize / 2);
            double[] doubleBuffer = new double[bufferSize];
            double sum = 0;
            for (int n = 0; n < bufferSize; n++)
            {
                double dB = 20 * Math.Log10(Math.Abs(floatBuffer[n]));
                //var delta = TimeSpan.FromSeconds((double)(bufferSize - 2 * n) / byteRate);
                //var time = e.OriginatingTime - delta;
                sum += dB;
                doubleBuffer[n] = dB;
            }
            var avgDB = sum / bufferSize;
            DecibelsOut.Post(doubleBuffer, e.OriginatingTime);
            AvgDecibelsOut.Post(avgDB, e.OriginatingTime);
        }
    }
}
