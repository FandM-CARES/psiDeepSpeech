using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace psiDeepSpeech
{
    public class DeepSpeechConfiguration
    {
        public DeepSpeechConfiguration()
        {

        }

        public string modelFileName { get; set; } = "output_graph.pbmm";

        public string scorerFileName { get; set; } = "kenlm.scorer";

        public uint beamWidth { get; set; } = 0;

        public string hotwordsFileName { get; set; } = "hotwords.json";
    }
}
