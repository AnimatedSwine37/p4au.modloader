using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace p4au.modloader
{
    public class PacInfo
    {
        public Dictionary<string, FileHashInfo> ContainedFileHashes { get; set; }
        public FileHashInfo HashInfo { get; set; }

        public PacInfo(Dictionary<string, FileHashInfo> ContainedFileHashes, FileHashInfo HashInfo)
        {
            this.ContainedFileHashes = ContainedFileHashes;
            this.HashInfo = HashInfo;
        }
    }
}
