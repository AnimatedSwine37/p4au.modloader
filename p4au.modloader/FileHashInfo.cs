using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace p4au.modloader
{
    /// <summary>
    /// A class for storing a Files hash and path for easy comparison of files
    /// </summary>
    public class FileHashInfo
    {
        public string Hash { get; set; }
        public string Path { get; set; }

        public FileHashInfo(string path, string hash)
        {
            Hash = hash;
            Path = path;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not FileHashInfo)
                return false;
            return Hash.Equals(((FileHashInfo)obj).Hash);
        }
    }
}
