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
            foreach(var file in toMerge)
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
                foreach (var file in Directory.EnumerateFiles(Path.Combine(modPath, "Redirector", "asset")))
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
            var paths = GetPaths();
            foreach(var mergeSet in toMerge)
            {
                if (!paths.Any(p => p.filepathMD5.Equals(mergeSet.Key, StringComparison.InvariantCultureIgnoreCase)))
                {
                    logger.WriteLine($"{mergeSet.Key} is not a valid file name, ignoring");
                    continue;
                }
                string friendlyPath = paths.First(p => p.filepathMD5.Equals(mergeSet.Key, StringComparison.InvariantCultureIgnoreCase)).filepath;
                logger.WriteLine($"{mergeSet.Key} => {friendlyPath}");
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
            // Step 1, calculate MD5 hash from input
            MD5 md5 = MD5.Create();
            byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            // Step 2, convert byte array to hex string
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }
            return sb.ToString();
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