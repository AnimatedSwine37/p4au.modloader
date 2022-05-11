namespace p4au.modloader
{
    /// <summary>
    /// A class for storing a Files hash and path for easy comparison of files
    /// </summary>
    public class FileHashInfo
    {
        public string Path { get; set; }
        public string Hash { get; set; }

        public FileHashInfo(string Path, string Hash)
        {
            this.Hash = Hash;
            this.Path = Path;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not FileHashInfo)
                return false;
            return Hash.Equals(((FileHashInfo)obj).Hash);
        }
    }
}
