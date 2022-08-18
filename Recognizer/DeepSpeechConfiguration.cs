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

        public bool storeInternalStreams { get; set; } = false;
        public string psiStoreOutName { get; set; } = null;
        public string psiStoreOutPath { get; set; } = null;

        public bool replayFromStore { get; set; } = false;
        public string psiStoreInPath { get; set; } = null;
        public string psiStoreInName { get; set; } = null;
        public string psiStoreInStream { get; set; } = null;

        public bool push2talk { get; set; } = false;

        public bool verbose { get; set; } = false;

    }
}
