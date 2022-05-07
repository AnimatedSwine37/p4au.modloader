using p4au.modloader.Utils;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;
using System;
using System.Collections.Generic;
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
        private ILogger logger;
        public Mod(IReloadedHooks hooks, ILogger logger, List<string> activeModPaths, string modLoaderPath)
        {
            this.modLoaderPath = modLoaderPath;
            this.logger = logger;
            var toMerge = GetFilesToMerge(activeModPaths);
            foreach (var file in toMerge)
                foreach (var filePath in file.Value)
                    logger.WriteLine(filePath);

            MergeFiles(toMerge);
        }

        /// <summary>
        /// Gets all of the files that may need to be merged by checking for duplicate files in active mod's Redirector folder
        /// </summary>
        /// <param name="activeModPaths">A list of the paths to every active mod</param>
        /// <returns>A Dictionary with the name of the file as the key and a list of all the occurences of the file as the value</returns>
        private Dictionary<string, List<string>> GetFilesToMerge(List<string> activeModPaths)
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
            Dictionary<string, List<string>> toMerge = new Dictionary<string, List<string>>();
            foreach (var file in allFiles)
            {
                if (toMerge.ContainsKey(Path.GetFileName(file)))
                    continue;
                var matchingFiles = allFiles.FindAll(f => f.EndsWith(Path.GetFileName(file)));
                if (matchingFiles.Count > 1)
                {
                    toMerge.Add(Path.GetFileName(file), matchingFiles);
                }
            }
            return toMerge;
        }

        private void MergeFiles(Dictionary<string, List<string>> toMerge)
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
            var paths = GetPaths();
            List<string> decryptList = new List<string>();
            Parallel.ForEach(toMerge, mergeSet =>
            {
                if (!paths.Any(p => p.filepathMD5.Equals(mergeSet.Key, StringComparison.InvariantCultureIgnoreCase)))
                {
                    //logger.WriteLine($"{mergeSet.Key} is not a valid file name, ignoring");
                    return;
                }
                // Get a copy of the original file and decrypt it
                string originalPath = Path.Combine("originals", mergeSet.Key);
                File.Copy(Path.Combine("asset", mergeSet.Key), originalPath, true);
                DecryptFile(originalPath);
                // Get the friendly name of the encrypted file
                string friendlyPath = paths.First(p => p.filepathMD5.Equals(mergeSet.Key, StringComparison.InvariantCultureIgnoreCase)).filepath;
                logger.WriteLine($"{mergeSet.Key} => {friendlyPath}");
                if (Path.GetExtension(friendlyPath) != ".pac")
                {
                    logger.WriteLine($"Skipping {friendlyPath} as it is not a pac");
                    return;
                }
                // Unpack the original file
                string originalDecryptedPath = Path.Combine("originals", friendlyPath);
                UnpackPac(originalDecryptedPath);
                UnpackPac(Path.ChangeExtension(originalDecryptedPath, null));
                Dictionary<string, List<FileHashInfo>> hashes = new Dictionary<string, List<FileHashInfo>>();
                // Decrypt and unpack the files
                foreach (var encryptedFile in mergeSet.Value)
                {
                    DecryptFile(encryptedFile);
                    string pacPath = GetDecryptedPath(encryptedFile, friendlyPath);
                    UnpackPac(pacPath);
                    string unpackedFolder = pacPath.Replace(".pac", "");
                    logger.WriteLine($"The unpacked folder is at {unpackedFolder}");
                    GetFileHashes(unpackedFolder, hashes);
                }

                // Compare the files with the originals
                bool repack = false;
                foreach(var fileSet in hashes)
                {
                    // Get the original hash
                    string originalFile = Path.Combine("originals", fileSet.Key);
                    string originalHash = GetFileHash(originalFile);
                    // Compare each file to merge with the originals
                    foreach(var file in fileSet.Value)
                    {
                        if(file.Hash != originalHash)
                        {
                            repack = true;
                            // Copy the edited file to our folder of files to merge
                            string mergedPath = Path.Combine("merged", fileSet.Key);
                            Directory.CreateDirectory(Path.GetDirectoryName(mergedPath));
                            File.Copy(file.Path, mergedPath, true);
                            logger.WriteLine($"Going to merge {file.Path}");
                        }
                    }
                }
                
                if(repack)
                {
                    // Repack the now merged pack directory
                    string mergedPac = RepackDirectory(friendlyPath);
                    // Copy the pac into the redirector of this mod renaming it to its encrypted name so it's picked up
                    EncryptFile(mergedPac);
                    File.Copy(Path.Combine("merged", Path.GetDirectoryName(friendlyPath)!, mergeSet.Key), Path.Combine(redirectorPath, mergeSet.Key), true);
                }

            });
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
        private Dictionary<string, List<FileHashInfo>> GetFileHashes(string dir, Dictionary<string, List<FileHashInfo>> hashes)
        {
            foreach(var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                string hash = GetFileHash(file);
                string relativePath = GetDataPath(file);
                if(hashes.ContainsKey(relativePath))
                {
                    hashes[relativePath].Add(new FileHashInfo(file, hash));
                }
                else
                {
                    hashes.Add(relativePath, new List<FileHashInfo> { new FileHashInfo(file, hash) });
                }
            }
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
            logger.WriteLine($"Packed {directory}");
        }

        /// <summary>
        /// Unpacks all the pacs in a directory recursively 
        /// </summary>
        /// <param name="pac">The pac file to unpack</param>
        private void UnpackPac(string pac)
        {
            RunArcSysCommand($"pac \"{pac}\" unpack -om Overwrite -c");
            logger.WriteLine($"Unpacked {pac}");
        }

        /// <summary>
        /// Decrypts a file
        /// </summary>
        /// <param name="file">The file to decrypt</param>
        private void DecryptFile(string file)
        {
            RunArcSysCommand($"crypt \"{file}\" -g p4u2 -om Overwrite -c -p \"{Path.Combine(modLoaderPath, "GeoArcSysAIOCLITool", "Paths", "P4U2 Paths.txt")}\"");
            logger.WriteLine($"Decrypted {file}");
        }

        /// <summary>
        /// Encrypts a file
        /// </summary>
        /// <param name="file">The file to encrypt</param>
        private void EncryptFile(string file)
        {
            RunArcSysCommand($"crypt \"{file}\" -g p4u2 -om Overwrite -c");
            logger.WriteLine($"Decrypted {file}");
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
                if(displayOutput)
                    logger.WriteLine(line);
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