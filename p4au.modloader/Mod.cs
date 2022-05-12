using p4au.modloader.Configuration;
using p4au.modloader.Utilities;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace p4au.modloader
{
    /// <summary>
    /// Your mod logic goes here.
    /// </summary>
    public class Mod
    {
        private string modLoaderPath;
        private CacheConfig cache;
        private List<PacInfo> toCache;
        public Mod(List<string> activeModPaths, string modLoaderPath, CacheConfig cache)
        {
            this.modLoaderPath = modLoaderPath;
            this.cache = cache;
            toCache = new List<PacInfo>();
            Utils.Log($"Finding files to merge");
            var toMerge = GetFilesToMerge(activeModPaths);
            Utils.Log($"Done finding files to merge");
            if (MergeSetIsEqual(toMerge, cache.LastModSetFiles))
            {
                Utils.Log($"The current mod set is the same as the last built one, skipping rebuild");
                return;
            }
            Utils.Log("Beginning file merging");
            MergeFiles(toMerge);
            if(toCache.Count > 0)
                cache.FileCache.AddRange(toCache);
            cache.LastModSetFiles = toMerge;
            cache.Save();
            Utils.Log("Finished merging and redirecting files");
        }

        /// <summary>
        /// Checks if two Dictionaries of files and hash info are equal (if two merge builds are the same)
        /// </summary>
        /// <param name="a">The first set of files to compare to <paramref name="b"/></param>
        /// <param name="b">The second set of files that will be compared</param>
        /// <returns>True if both sets are exactly the same</returns>
        private bool MergeSetIsEqual(Dictionary<string, List<FileHashInfo>> a, Dictionary<string, List<FileHashInfo>> b)
        {
            foreach(var set in a)
            {
                if (!b.TryGetValue(set.Key, out var filesB))
                    return false;
                foreach (var file in set.Value)
                    if (!filesB.Contains(file))
                        return false;
            }
            return true;
        }

        /// <summary>
        /// Gets all of the files that may need to be merged by checking for duplicate files in active mod's Redirector folder
        /// </summary>
        /// <param name="activeModPaths">A list of the paths to every active mod</param>
        /// <returns>A Dictionary with the name of the file as the key and a list of all the occurences of the file as the value</returns>
        private Dictionary<string, List<FileHashInfo>> GetFilesToMerge(List<string> activeModPaths)
        {
            // Get all files
            List<string> allFiles = new List<string>();
            foreach (string modPath in activeModPaths)
            {
                string assetPath = Path.Combine(modPath, "Redirector", "asset");
                if (!Directory.Exists(assetPath))
                    continue;
                foreach (var file in Directory.EnumerateFiles(assetPath))
                {
                    allFiles.Add(file);
                }
            }

            // Get files that appear in multiple mods
            Dictionary<string, List<FileHashInfo>> toMerge = new Dictionary<string, List<FileHashInfo>>();
            foreach (var file in allFiles)
            {
                if (toMerge.ContainsKey(Path.GetFileName(file)))
                    continue;
                var matchingFiles = allFiles.FindAll(f => f.EndsWith(Path.GetFileName(file)));
                if (matchingFiles.Count > 1)
                {
                    List<FileHashInfo> fileHashes = GetFileHashes(matchingFiles);
                    toMerge.Add(Path.GetFileName(file), fileHashes);
                }
            }
            return toMerge;
        }

        private void SetupDirectories()
        {
            if (!Directory.Exists("originals"))
                Directory.CreateDirectory("originals");
            string redirectorPath = Path.Combine(modLoaderPath, "Redirector", "asset");
            if (Directory.Exists(redirectorPath))
                Directory.Delete(redirectorPath, true);
            Directory.CreateDirectory(redirectorPath);
            if (Directory.Exists("merged"))
                Directory.Delete("merged", true);
            Directory.CreateDirectory("merged");
        }

        private void MergeFiles(Dictionary<string, List<FileHashInfo>> toMerge)
        {
            SetupDirectories();
            var paths = GetPaths();
            Parallel.ForEach(toMerge, mergeSet =>
            {
                Utils.LogVerbose($"Beginning merging of {mergeSet.Key}");
                // Ignore files that aren't used in P4AU
                if (!paths.Any(p => p.filepathMD5.Equals(mergeSet.Key, StringComparison.InvariantCultureIgnoreCase)))
                {
                    Utils.LogVerbose($"Cancelling merging {mergeSet.Key} as it is not a valid P4AU file");
                    return;
                }

                string? friendlyPath = UnpackOriginals(mergeSet.Key, paths);
                if (friendlyPath == null)
                {
                    Utils.LogVerbose($"Cancelling merging {mergeSet.Key} as it is not a valid P4AU file");
                    return;
                }

                var hashes = UnpackFilesToMerge(mergeSet.Value, friendlyPath);

                if (CompareFiles(hashes))
                {
                    // Repack the now merged pack directory
                    string mergedPac = RepackDirectory(friendlyPath);
                    // Copy the pac into the redirector of this mod renaming it to its encrypted name so it's picked up
                    EncryptFile(mergedPac);
                    File.Copy(Path.Combine("merged", Path.GetDirectoryName(friendlyPath)!, mergeSet.Key), Path.Combine(Path.Combine(modLoaderPath, "Redirector", "asset"), mergeSet.Key), true);
                }
                Utils.LogVerbose($"Done merging {mergeSet.Key}");
            });
        }

        /// <summary>
        /// Compares the files from a dictionary of them, preparing any that should be merged
        /// </summary>
        /// <param name="hashes">A Dictionary with the file name (such as "data\char\char_mi_pal\mi00_00.hpl") as the key 
        /// and a list of <see cref="FileHashInfo"/> representing the copies of that file from different mods</param>
        /// <returns>True if there were files that need to be merged, false otherwise</returns>
        private bool CompareFiles(Dictionary<string, List<FileHashInfo>> hashes)
        {
            bool repack = false;
            foreach (var fileSet in hashes)
            {
                // Get the original hash
                string originalFile = Path.Combine("originals", fileSet.Key);
                string originalHash = GetFileHash(originalFile);
                // Compare each file to merge with the originals
                foreach (var file in fileSet.Value)
                {
                    if (file.Hash != originalHash)
                    {
                        repack = true;
                        // Copy the edited file to our folder of files to merge
                        string mergedPath = Path.Combine("merged", fileSet.Key);
                        Directory.CreateDirectory(Path.GetDirectoryName(mergedPath)!);
                        File.Copy(file.Path, mergedPath, true);
                    }
                }
            }
            return repack;
        }

        /// <summary>
        /// Unpacks the files that are going to be merged
        /// </summary>
        /// <param name="files">The files to unpack</param>
        /// <param name="friendlyPath">The friendly path of the pac that contains the files</param>
        /// <returns>A Dictionary with the file name (such as "data\char\char_mi_pal\mi00_00.hpl") as the key 
        /// and a list of <see cref="FileHashInfo"/> representing the copies of that file from different mods</returns>
        private Dictionary<string, List<FileHashInfo>> UnpackFilesToMerge(List<FileHashInfo> files, string friendlyPath)
        {
            Utils.LogVerbose($"Starting to unpack files for {friendlyPath}");
            Stopwatch watch = new Stopwatch();
            watch.Start();
            Dictionary<string, List<FileHashInfo>> hashes = new Dictionary<string, List<FileHashInfo>>();
            foreach (var encryptedFile in files)
            {
                // Check if this exact file has already been processed in a previous run
                var cachedInfo = cache.FileCache.FirstOrDefault(f => f.HashInfo.Path == encryptedFile.Path);
                if (cachedInfo != null && cachedInfo.HashInfo.Hash == encryptedFile.Hash)
                {
                    Utils.LogVerbose($"Skipping {encryptedFile} as it has already been unpacked");
                    foreach(var file in cachedInfo.ContainedFileHashes)
                        if (hashes.ContainsKey(file.Key))
                            hashes[file.Key].Add(file.Value);
                        else
                            hashes.Add(file.Key, new List<FileHashInfo> { file.Value });
                    continue;
                }
                DecryptFile(encryptedFile.Path);
                string pacPath = GetDecryptedPath(encryptedFile.Path, friendlyPath);
                UnpackPac(pacPath);
                string unpackedFolder = pacPath.Replace(".pac", "");
                Utils.LogVerbose($"The unpacked folder is at {unpackedFolder}");
                var newHashes = GetAllFileHashes(unpackedFolder, hashes);
                // Store info about the file that we just processed in the cachje
                if (cachedInfo != null)
                {
                    cachedInfo.HashInfo.Hash = encryptedFile.Hash;
                    cachedInfo.ContainedFileHashes = newHashes;
                }
                else 
                    toCache.Add(new PacInfo(newHashes, encryptedFile));
            }
            watch.Stop();
            Utils.LogVerbose($"Finished unpacking files for {friendlyPath} in {watch.ElapsedMilliseconds}ms");
            return hashes;
        }

        /// <summary>
        /// Unpack any needed original files for the current set of files that are being merged
        /// </summary>
        /// <param name="mergeSet"></param>
        /// <param name="paths"></param>
        /// <returns>The friendly path to the file that was unpacked or null if the file wasn't a pac (so it shouldn't be considered for merging)</returns>
        private string? UnpackOriginals(string encryptedFile, List<FilePaths> paths)
        {
            // Get the friendly name of the encrypted file
            string friendlyPath = paths.First(p => p.filepathMD5.Equals(encryptedFile, StringComparison.InvariantCultureIgnoreCase)).filepath;
            string originalPath = Path.Combine("originals", encryptedFile);
            if (File.Exists(originalPath))
                return friendlyPath;
            // Get a copy of the original file and decrypt it
            File.Copy(Path.Combine("asset", encryptedFile), originalPath, true);
            DecryptFile(originalPath);
            if (Path.GetExtension(friendlyPath) != ".pac")
            {
                Utils.LogVerbose($"Skipping {friendlyPath} as it is not a pac");
                return null;
            }
            // Unpack the original file
            string originalDecryptedPath = Path.Combine("originals", friendlyPath);
            UnpackPac(originalDecryptedPath);
            UnpackPac(Path.ChangeExtension(originalDecryptedPath, null));
            return friendlyPath;
        }

        /// <summary>
        /// Merges a directory back into a pac adding any necessary files from the originals folder
        /// </summary>
        /// <param name="friendlyPath">The friendly path (such as "data\char\char_ag_pal") to the directory inside of the merged folder to repack</param>
        /// <returns>The full path to the newly created pac file</returns>
        private string RepackDirectory(string friendlyPath)
        {
            string mergedDir = Path.Combine("merged", Path.ChangeExtension(friendlyPath, null));
            // Add any missing files
            FileUtils.CopyDirectory(Path.Combine("originals", Path.ChangeExtension(friendlyPath, null)), mergedDir, true, false);
            PackDirectory(mergedDir);
            return Path.ChangeExtension(mergedDir, ".pac");
        }

        /// <summary>
        /// Converts the path to an encrypted to file to that of its decrypted counterpart
        /// </summary>
        /// <param name="encryptedPath">The path of the encrypted file</param>
        /// <param name="friendlyPath">The relative path of the friendly file such as "data\char\char_ag_pal.pac"</param>
        /// <returns>The full path to the decrypted file</returns>
        private string GetDecryptedPath(string encryptedPath, string friendlyPath)
        {
            return Path.Combine(encryptedPath.Substring(0, encryptedPath.LastIndexOf("asset") + 5), friendlyPath);
        }

        /// <summary>
        /// Gets the hashes of all of the files in a directory recursively and adds them to a dictionary
        /// </summary>
        /// <param name="dir">The directory to search through</param>
        /// <param name="hashes">A Dictionary with the file name (such as "data\char\char_mi_pal\mi00_00.hpl") as the key 
        /// and a list of <see cref="FileHashInfo"/> representing the copies of that file from different mods</param>
        /// <returns>A separate Dictionary of all of the newly added hashes in the same format as <paramref name="hashes"/></returns>
        private Dictionary<string, FileHashInfo> GetAllFileHashes(string dir, Dictionary<string, List<FileHashInfo>> hashes)
        {
            Stopwatch watch = Stopwatch.StartNew();
            Dictionary<string, FileHashInfo> newHashes = new();
            foreach (var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                string hash = GetFileHash(file);
                string relativePath = GetDataPath(file);
                if (hashes.ContainsKey(relativePath))
                    hashes[relativePath].Add(new FileHashInfo(file, hash));
                else
                    hashes.Add(relativePath, new List<FileHashInfo> { new FileHashInfo(file, hash) });

                newHashes.Add(relativePath, new FileHashInfo(file, hash));
            }
            watch.Stop();
            Utils.LogVerbose($"Finished calculating {Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories).Length} file hashes in {watch.ElapsedMilliseconds}ms");
            return newHashes;
        }

        /// <summary>
        /// Gets a list containing the <see cref="FileHashInfo"/> for every file in the supplied list of files
        /// </summary>
        /// <param name="files">A <see cref="List{String}"/> of full file paths that willl be used</param>
        /// <returns>A List containing a <see cref="FileHashInfo"/> for every file listed in <paramref name="files"/></returns>
        private List<FileHashInfo> GetFileHashes(List<string> files)
        {
            List<FileHashInfo> hashes = new List<FileHashInfo>();
            Parallel.ForEach(files, file => hashes.Add(new FileHashInfo(file, GetFileHash(file))));
            return hashes;
        }

        /// <summary>
        /// Converts a full file path to one relative to the data folder such as "data\char\char_mi_pal.pac"
        /// </summary>
        /// <param name="filePath">The path of the file to get the relative path of</param>
        /// <returns>A string containing the file path relative to the data folder</returns>
        private string GetDataPath(string filePath)
        {
            return filePath.Substring(filePath.LastIndexOf("data"));
        }

        /// <summary>
        /// Packs a directory into a pac file
        /// </summary>
        /// <param name="directory">The directory to pack</param>
        private void PackDirectory(string directory)
        {
            RunArcSysCommand($"pac \"{directory}\" -om Overwrite -c");
            Utils.LogVerbose($"Packed {directory}");
        }

        /// <summary>
        /// Unpacks all the pacs in a directory recursively 
        /// </summary>
        /// <param name="pac">The pac file to unpack</param>
        private void UnpackPac(string pac)
        {
            RunArcSysCommand($"pac \"{pac}\" unpack -om Overwrite -c");
            Utils.LogVerbose($"Unpacked {pac}");
        }

        /// <summary>
        /// Decrypts a file
        /// </summary>
        /// <param name="file">The file to decrypt</param>
        private void DecryptFile(string file)
        {
            RunArcSysCommand($"crypt \"{file}\" -g p4u2 -om Overwrite -c -p \"{Path.Combine(modLoaderPath, "GeoArcSysAIOCLITool", "Paths", "P4U2 Paths.txt")}\"");
            Utils.LogVerbose($"Decrypted {file}");
        }

        /// <summary>
        /// Encrypts a file
        /// </summary>
        /// <param name="file">The file to encrypt</param>
        private void EncryptFile(string file)
        {
            RunArcSysCommand($"crypt \"{file}\" -g p4u2 -om Overwrite -c");
            Utils.LogVerbose($"Encrypted {file}");
        }

        private void RunArcSysCommand(string args, bool displayOutput = false)
        {
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            startInfo.FileName = Path.Combine(modLoaderPath, "GeoArcSysAIOCLITool", "GeoArcSysAIOCLITool.exe");
            startInfo.Arguments = args;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            process.StartInfo = startInfo;
            process.Start();
            while (!process.StandardOutput.EndOfStream)
            {
                string? line = process.StandardOutput.ReadLine();
                if (displayOutput)
                    Utils.LogVerbose(line);
                if (line != null && line == "Complete!")
                    process.Kill();
            }

        }

        private List<FilePaths> GetPaths()
        {
            var pathList = new List<FilePaths>();
            using (TextReader reader = File.OpenText(Path.Combine(modLoaderPath, "GeoArcSysAIOCLITool", "Paths", "P4U2 Paths.txt")))
            {
                var pattern = new Regex("[/\"]|[/]{2}");
                while (reader.Peek() >= 0)
                {
                    var line = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        line = pattern.Replace(line, "/").ToLower();
                        try
                        {
                            Path.GetFullPath(line);
                            var lineMD5 = CreateMD5Hash(line);
                            line = line.Replace("/", "\\");
                            pathList.Add(new FilePaths(line, lineMD5));
                        }
                        catch
                        {
                        }
                    }
                }
            }

            return pathList;
        }

        private string CreateMD5Hash(string input)
        {
            MD5 md5 = MD5.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            return ByteArrayToString(hashBytes);
        }

        private string ByteArrayToString(byte[] bytes)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private string GetFileHash(string file)
        {
            byte[] exeBytes = File.ReadAllBytes(file);
            byte[] hashBytes = new MD5CryptoServiceProvider().ComputeHash(exeBytes);
            return ByteArrayToString(hashBytes);
        }

        private struct FilePaths
        {
            public string filepath, filepathMD5;

            public FilePaths(string p1, string p2)
            {
                filepath = p1;
                filepathMD5 = p2;
            }
        }

    }
}