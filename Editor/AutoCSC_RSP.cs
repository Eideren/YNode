using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace YNode.Editor
{
    [InitializeOnLoad]
    public static class AutoCSC_RSP
    {
        static AutoCSC_RSP()
        {
            Task.Run(ValidateCSCRSP);
            return;

            static void ValidateCSCRSP()
            {
                try
                {
                    foreach (var subDir in new[] { "Assets", "Packages" })
                    {
                        var fullPath = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, subDir);
                        foreach (string asmdefPath in Directory.EnumerateFiles(fullPath, "*.asmdef", SearchOption.AllDirectories))
                        {
                            if (Regex.Match(File.ReadAllText(asmdefPath), """\"name\"\s*:\s*\"([^"]*)""") is not { } m || !m.Success)
                            {
                                Debug.LogWarning($"Could not find assembly name in assembly definition at path {asmdefPath}");
                                continue;
                            }

                            if (Path.GetFileNameWithoutExtension(asmdefPath).StartsWith("UnityEngine.")
                                || Path.GetFileNameWithoutExtension(asmdefPath).StartsWith("UnityEditor.")
                                || Path.GetFileNameWithoutExtension(asmdefPath).StartsWith("Unity."))
                                continue;

                            var csc = Path.Combine(Path.GetDirectoryName(asmdefPath), "csc.rsp");
                            var cscMeta = Path.Combine(Path.GetDirectoryName(asmdefPath), "csc.rsp.meta");
                            string content;
                            try
                            {
                                content = File.ReadAllText(csc);
                            }
                            catch (Exception)
                            {
                                content = "";
                            }

                            var newContent = content;
                            if (content.Contains("-langVersion:") == false)
                                newContent = $"{newContent.Trim()} -langVersion:preview";

                            if (content.Contains("-nullable") == false)
                                newContent = $"{newContent.Trim()} -nullable";

                            if (content.Contains("-doc:") == false)
                                newContent = $"{newContent.Trim()} -doc:Library/StreamingAssets/{m.Groups[1]}.xml";

                            if (content.Contains("-nowarn:") == false)
                                newContent = $"{newContent.Trim()} -nowarn:1591";

                            if (content == newContent)
                                continue;

                            File.WriteAllText(csc, newContent);

                            if (File.Exists(cscMeta) == false)
                                File.WriteAllText(cscMeta, @$"fileFormatVersion: 2
    guid: {Guid.NewGuid().ToString().Replace("-", "")}
    AssemblyDefinitionImporter:
      externalObjects: {{}}
      userData:
      assetBundleName:
      assetBundleVariant:
    ");
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            }
        }
    }
}
