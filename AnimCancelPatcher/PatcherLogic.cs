using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Collections.Generic;

namespace AnimCancelPatcher
{
    public class PatcherLogic
    {
        private readonly string _uassetGuiPath;

        public PatcherLogic()
        {
            _uassetGuiPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UassetGUI", "UassetGUI.exe");
        }

        public void ProcessComboCancel(CharacterConfig config, int targetStage, string rootGameDirectory)
        {
            if (!config.ComboAssets.TryGetValue(targetStage, out string? assetName) || string.IsNullOrEmpty(assetName))
            {
                throw new Exception($"Asset name for stage {targetStage} is not defined.");
            }

            string assetDir = Path.Combine(rootGameDirectory, config.RelativeAssetDirectory);
            string uassetPath = Path.Combine(assetDir, assetName + ".uasset");
            string tempJsonPath = Path.Combine(assetDir, assetName + "_temp.json");

            if (!File.Exists(uassetPath))
            {
                throw new Exception($"Target file not found: {uassetPath}");
            }
            RunUAssetGUI($"tojson \"{uassetPath}\" \"{tempJsonPath}\" VER_UE4_27");
            ModifyJsonForCancel(tempJsonPath);
            RunUAssetGUI($"fromjson \"{tempJsonPath}\" \"{uassetPath}\"");
            // 変換終わったらtempはもう要らない
            if (File.Exists(tempJsonPath))
            {
                File.Delete(tempJsonPath);
            }
        }

        private void RunUAssetGUI(string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = _uassetGuiPath,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process? process = Process.Start(psi))
            {
                if (process == null) throw new Exception("Failed to start UAssetGUI.");

                string stdout = "";
                string stderr = "";
                var stdoutTask = System.Threading.Tasks.Task.Run(() => stdout = process.StandardOutput.ReadToEnd());
                var stderrTask = System.Threading.Tasks.Task.Run(() => stderr = process.StandardError.ReadToEnd());

                process.WaitForExit();
                System.Threading.Tasks.Task.WaitAll(stdoutTask, stderrTask);
                // Console.WriteLine(stdout);

                if (process.ExitCode != 0)
                {
                    throw new Exception($"UAssetGUI execution failed. Exit code: {process.ExitCode}\n{stderr}");
                }
            }
        }

        private void ModifyJsonForCancel(string jsonPath)
        {
            string jsonContent = File.ReadAllText(jsonPath);
            JObject root = JObject.Parse(jsonContent);

            var exports = root["Exports"] as JArray;
            if (exports == null) return;

            JArray GetNotifyFields(JToken notify)
            {
                if (notify["Value"] != null && notify["Value"] is JArray arr)
                {
                    return arr;
                }
                return new JArray();
            }

            string GetNotifyName(JToken notify)
            {
                var fields = GetNotifyFields(notify);
                foreach (var f in fields)
                {
                    if (f is JObject obj)
                    {
                        if (obj["Name"] != null && obj["Name"].ToString() == "NotifyName")
                        {
                            if (obj["Value"] != null)
                            {
                                return obj["Value"].ToString();
                            }
                        }
                    }
                }
                return "";
            }

            float GetLinkValue(JToken notify)
            {
                var fields = GetNotifyFields(notify);
                foreach (var f in fields)
                {
                    if (f is JObject obj)
                    {
                        if (obj["Name"] != null && obj["Name"].ToString() == "LinkValue")
                        {


                            var token = obj["Value"];
                            if (token == null) return 0f;

                            // uasset->json変換だとLinkValueが"+0.25"みたいに文字列で来ることがある
                            if (token.Type == JTokenType.String)
                            {
                                string s = token.ToString();
                                if (s.StartsWith("+")) s = s.Substring(1);
                                return float.Parse(s);
                            }
                            return token.ToObject<float>();
                        }
                    }
                }
                return 0f;
            }

            void SetLinkValue(JToken notify, float value)
            {
                var fields = GetNotifyFields(notify);
                foreach (var f in fields)
                {
                    if (f is JObject obj)
                    {
                        if (obj["Name"] != null && obj["Name"].ToString() == "LinkValue")
                        {
                            obj["Value"] = value;
                        }
                        // EndLinkは終了時刻側。State系のNotifyだと両方書き換えないとGUI上ズレる
                        else if (obj["Name"] != null && obj["Name"].ToString() == "EndLink")
                        {
                            if (obj["Value"] is JArray endLinkValues)
                            {
                                foreach (var subF in endLinkValues)
                                {
                                    if (subF is JObject subObj && subObj["Name"] != null && subObj["Name"].ToString() == "LinkValue")
                                    {
                                        subObj["Value"] = value;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            void SetNotifyName(JToken notify, string value)
            {
                var fields = GetNotifyFields(notify);
                foreach (var f in fields)
                {
                    if (f is JObject obj && obj["Name"] != null && obj["Name"].ToString() == "NotifyName")
                    {
                        obj["Value"] = value;
                    }
                }
            }
            void SetNotifyObjectRef(JToken notify, int exportIndex1Based)
            {
                var fields = GetNotifyFields(notify);
                foreach (var f in fields)
                {
                    if (f is JObject obj && obj["Name"] != null && obj["Name"].ToString() == "Notify")
                    {
                        obj["Value"] = exportIndex1Based;
                    }
                }
            }
            void ResetNotifyStateClass(JToken notify)
            {
                var fields = GetNotifyFields(notify);
                foreach (var f in fields)
                {
                    if (f is JObject obj && obj["Name"] != null && obj["Name"].ToString() == "NotifyStateClass")
                    {
                        obj["Value"] = 0;
                    }
                }
            }
            // 拾えなかったのでStartsWithにした
            bool hasReturnExport = false;
            foreach (var e in exports)
            {
                if (e["ObjectName"] != null)
                {
                    string n = e["ObjectName"].ToString();
                    if (n.StartsWith("AN_ReturnToNormal"))
                    {
                        hasReturnExport = true;
                        break;
                    }
                }
            }

            bool hasEndExport = false;
            foreach (var e in exports)
            {
                if (e["ObjectName"] != null)
                {
                    string n = e["ObjectName"].ToString();
                    if (n.StartsWith("AN_AttackEnd"))
                    {
                        hasEndExport = true;
                        break;
                    }
                }
            }

            //どっちか無かったらエクスポートから作り直す扱いにする
            bool needsExportCreation = !hasReturnExport || !hasEndExport;

            JObject? montageExport = null;
            foreach (var e in exports)
            {
                if (e is JObject obj)
                {
                    if (obj["ObjectName"] != null)
                    {
                        string n = obj["ObjectName"].ToString();
                        // Default__Montage系を弾かないとテンプレ側を掴んでしまう
                        if (n.Contains("Montage") && !n.Contains("Default"))
                        {
                            montageExport = obj;
                            break;
                        }
                    }
                }
            }
            if (montageExport == null) return;

            int montageExportIndex1Based = exports.IndexOf(montageExport) + 1;

            JArray? notifiesList = null;
            foreach (var export in exports)
            {
                if (export is JObject obj && obj["Data"] is JArray dataArray)
                {
                    foreach (var d in dataArray)
                    {
                        if (d is JObject dObj && dObj["Name"] != null && dObj["Name"].ToString() == "Notifies")
                        {
                            notifiesList = dObj["Value"] as JArray;
                            break;
                        }
                    }
                    if (notifiesList != null) break;
                }
            }
            if (notifiesList == null) return;

            float attackStartTime = 0.25f; // 見つからなかった時の保険値
            foreach (var n in notifiesList)
            {
                if (GetNotifyName(n) == "ANS_Attack")
                {
                    attackStartTime = GetLinkValue(n);
                    break;
                }
            }
            float cancelTime = attackStartTime + 0.02f;
            foreach (var notifyToken in notifiesList)
            {
                string notifyName = GetNotifyName(notifyToken);

                var fields = GetNotifyFields(notifyToken);
                foreach (var f in fields)
                {
                    if (f is JObject obj && obj["Name"] != null && obj["Name"].ToString() == "TriggerTimeOffset")
                    {
                        obj["Value"] = 0.0;
                    }
                }

                if (notifyName == "AN_AttackComboChain")
                {
                    SetLinkValue(notifyToken, cancelTime);
                }

                if (notifyName == "ANS_AttackComboInputSpan")
                {
                    float val = cancelTime - 0.1f;
                    if (val < 0) val = 0; //マイナスは丸める
                    SetLinkValue(notifyToken, val);
                }
            }

            if (needsExportCreation)
            {
                JToken? existingReturn = null;
                foreach (var n in notifiesList) if (GetNotifyName(n) == "AN_ReturnToNormal") { existingReturn = n; break; }

                JToken? existingEnd = null;
                foreach (var n in notifiesList) if (GetNotifyName(n) == "AN_AttackEnd") { existingEnd = n; break; }

                JToken fallback = notifiesList.First();

                JObject? templateReturn = existingReturn != null ? existingReturn.DeepClone() as JObject
                                         : existingEnd != null ? existingEnd.DeepClone() as JObject
                                         : fallback.DeepClone() as JObject;

                JObject? templateEnd = existingEnd != null ? existingEnd.DeepClone() as JObject
                                      : existingReturn != null ? existingReturn.DeepClone() as JObject
                                      : fallback.DeepClone() as JObject;

                List<JToken> toRemove = new List<JToken>();
                foreach (var n in notifiesList)
                {
                    string nm = GetNotifyName(n);
                    if (nm == "AN_ReturnToNormal" || nm == "AN_AttackEnd")
                    {
                        toRemove.Add(n);
                    }
                }
                foreach (var item in toRemove)
                {
                    notifiesList.Remove(item);
                }
                // 古いNotify参照を残したままExport作り直すとIndexがズレて壊れるので
                // 先に消してから下で作り直す。順番大事。

                var imports = root["Imports"] as JArray;
                if (imports == null) return;

                int FindImportIndex(string objectName)
                {
                    for (int i = 0; i < imports.Count; i++)
                    {
                        if (imports[i]["ObjectName"] != null && imports[i]["ObjectName"].ToString() == objectName)
                        {
                            return -(i + 1);
                        }
                    }
                    return 0;
                }

                int returnClassIdx = FindImportIndex("AN_ReturnToNormal");
                int returnTemplateIdx = FindImportIndex("Default__AN_ReturnToNormal");
                int endClassIdx = FindImportIndex("AN_AttackEnd");
                int endTemplateIdx = FindImportIndex("Default__AN_AttackEnd");

                if (returnClassIdx == 0 || returnTemplateIdx == 0 || endClassIdx == 0 || endTemplateIdx == 0)
                {
                    // Import無いキャラは初回でここに来る。手動で組み立てて追加
                    int inGameIdx = FindImportIndex("/Script/InGameModule");

                    if (returnClassIdx == 0)
                    {
                        imports.Add(JObject.Parse($@"
                            {{""$type"": ""UAssetAPI.Import, UAssetAPI"",
                              ""ObjectName"": ""AN_ReturnToNormal"",
                              ""OuterIndex"": {inGameIdx},
                              ""ClassPackage"": ""/Script/CoreUObject"",
                              ""ClassName"": ""Class"",
                              ""PackageName"": null,
                              ""bImportOptional"": false}}"));
                        returnClassIdx = -(imports.Count);
                    }
                    if (returnTemplateIdx == 0)
                    {
                        imports.Add(JObject.Parse($@"
                            {{""$type"": ""UAssetAPI.Import, UAssetAPI"",
                              ""ObjectName"": ""Default__AN_ReturnToNormal"",
                              ""OuterIndex"": {inGameIdx},
                              ""ClassPackage"": ""/Script/InGameModule"",
                              ""ClassName"": ""AN_ReturnToNormal"",
                              ""PackageName"": null,
                              ""bImportOptional"": false}}"));
                        returnTemplateIdx = -(imports.Count);
                    }
                    if (endClassIdx == 0)
                    {
                        imports.Add(JObject.Parse($@"
                            {{""$type"": ""UAssetAPI.Import, UAssetAPI"",
                              ""ObjectName"": ""AN_AttackEnd"",
                              ""OuterIndex"": {inGameIdx},
                              ""ClassPackage"": ""/Script/CoreUObject"",
                              ""ClassName"": ""Class"",
                              ""PackageName"": null,
                              ""bImportOptional"": false}}"));
                        endClassIdx = -(imports.Count);
                    }
                    if (endTemplateIdx == 0)
                    {
                        imports.Add(JObject.Parse($@"
                            {{""$type"": ""UAssetAPI.Import, UAssetAPI"",
                              ""ObjectName"": ""Default__AN_AttackEnd"",
                              ""OuterIndex"": {inGameIdx},
                              ""ClassPackage"": ""/Script/InGameModule"",
                              ""ClassName"": ""AN_AttackEnd"",
                              ""PackageName"": null,
                              ""bImportOptional"": false}}"));
                        endTemplateIdx = -(imports.Count);
                    }
                }

                int returnExportIndex1Based = exports.Count + 1;
                exports.Add(JObject.Parse($@"
                    {{""$type"": ""UAssetAPI.ExportTypes.NormalExport, UAssetAPI"",
                      ""Data"": [],
                      ""ObjectGuid"": null,
                      ""SerializationControl"": ""NoExtension"",
                      ""Operation"": ""None"",
                      ""HasLeadingFourNullBytes"": false,
                      ""ObjectName"": ""AN_ReturnToNormal_0"",
                      ""OuterIndex"": {montageExportIndex1Based},
                      ""ClassIndex"": {returnClassIdx},
                      ""SuperIndex"": 0,
                      ""TemplateIndex"": {returnTemplateIdx},
                      ""ObjectFlags"": ""RF_Public, RF_Transactional"",
                      ""SerialSize"": 0,
                      ""SerialOffset"": 0,
                      ""ScriptSerializationStartOffset"": 0,
                      ""ScriptSerializationEndOffset"": 0,
                      ""bForcedExport"": false,
                      ""bNotForClient"": false,
                      ""bNotForServer"": false,
                      ""PackageGuid"": ""{{00000000-0000-0000-0000-000000000000}}"",
                      ""IsInheritedInstance"": false,
                      ""PackageFlags"": ""PKG_None"",
                      ""bNotAlwaysLoadedForEditorGame"": true,
                      ""bIsAsset"": false,
                      ""GeneratePublicHash"": false,
                      ""SerializationBeforeSerializationDependencies"": [],
                      ""CreateBeforeSerializationDependencies"": [],
                      ""SerializationBeforeCreateDependencies"": [{returnClassIdx}, {returnTemplateIdx}],
                      ""CreateBeforeCreateDependencies"": [{montageExportIndex1Based}],
                      ""Extras"": """"}}"));

                int endExportIndex1Based = exports.Count + 1;
                exports.Add(JObject.Parse($@"
                    {{""$type"": ""UAssetAPI.ExportTypes.NormalExport, UAssetAPI"",
                      ""Data"": [],
                      ""ObjectGuid"": null,
                      ""SerializationControl"": ""NoExtension"",
                      ""Operation"": ""None"",
                      ""HasLeadingFourNullBytes"": false,
                      ""ObjectName"": ""AN_AttackEnd_0"",
                      ""OuterIndex"": {montageExportIndex1Based},
                      ""ClassIndex"": {endClassIdx},
                      ""SuperIndex"": 0,
                      ""TemplateIndex"": {endTemplateIdx},
                      ""ObjectFlags"": ""RF_Public, RF_Transactional"",
                      ""SerialSize"": 0,
                      ""SerialOffset"": 0,
                      ""ScriptSerializationStartOffset"": 0,
                      ""ScriptSerializationEndOffset"": 0,
                      ""bForcedExport"": false,
                      ""bNotForClient"": false,
                      ""bNotForServer"": false,
                      ""PackageGuid"": ""{{00000000-0000-0000-0000-000000000000}}"",
                      ""IsInheritedInstance"": false,
                      ""PackageFlags"": ""PKG_None"",
                      ""bNotAlwaysLoadedForEditorGame"": true,
                      ""bIsAsset"": false,
                      ""GeneratePublicHash"": false,
                      ""SerializationBeforeSerializationDependencies"": [],
                      ""CreateBeforeSerializationDependencies"": [],
                      ""SerializationBeforeCreateDependencies"": [{endClassIdx}, {endTemplateIdx}],
                      ""CreateBeforeCreateDependencies"": [{montageExportIndex1Based}],
                      ""Extras"": """"}}"));

                if (root["NameMap"] is JArray nameMap)
                {
                    //NameMapに無い名前を後で参照するとUAssetGUI側でバグるから念のため全部足す
                    string[] newNames = { "AN_ReturnToNormal", "AN_AttackEnd",
                                          "Default__AN_ReturnToNormal", "Default__AN_AttackEnd" };
                    foreach (string name in newNames)
                    {
                        bool found = false;
                        foreach (var x in nameMap)
                        {
                            if (x.ToString() == name) found = true;
                        }
                        if (!found) nameMap.Add(name);
                    }
                }

                var newReturn = templateReturn?.DeepClone() as JObject;
                var newEnd = templateEnd?.DeepClone() as JObject;

                if (newReturn != null)
                {
                    SetNotifyName(newReturn, "AN_ReturnToNormal");
                    SetLinkValue(newReturn, cancelTime);
                    SetNotifyObjectRef(newReturn, returnExportIndex1Based);
                    ResetNotifyStateClass(newReturn);
                    notifiesList.Add(newReturn);
                }
                if (newEnd != null)
                {
                    SetNotifyName(newEnd, "AN_AttackEnd");
                    SetLinkValue(newEnd, cancelTime + 0.15f);
                    SetNotifyObjectRef(newEnd, endExportIndex1Based);
                    ResetNotifyStateClass(newEnd);
                    notifiesList.Add(newEnd);
                }

                // NextSectionNameが残ってるとコンボ抜けた後に変な遷移するからNone
                foreach (var export in exports)
                {
                    if (export is JObject obj && obj["Data"] is JArray dataArray)
                    {
                        foreach (var d in dataArray)
                        {
                            if (d is JObject dObj && dObj["Name"] != null && dObj["Name"].ToString() == "CompositeSections")
                            {
                                if (dObj["Value"] is JArray sections)
                                {
                                    foreach (var sec in sections)
                                    {
                                        if (sec is JObject sObj && sObj["Value"] is JArray inner)
                                        {
                                            foreach (var innerF in inner)
                                            {
                                                if (innerF is JObject iObj && iObj["Name"] != null && iObj["Name"].ToString() == "NextSectionName")
                                                {
                                                    iObj["Value"] = "None";
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // 既にExportがある場合はNotify差し替えだけで済む
                int FindExportIndex1Based(string prefix)
                {
                    for (int i = 0; i < exports.Count; i++)
                    {
                        if (exports[i]["ObjectName"] != null && exports[i]["ObjectName"].ToString().StartsWith(prefix))
                        {
                            return i + 1;
                        }
                    }
                    return 0;
                }

                int returnExportIdx = FindExportIndex1Based("AN_ReturnToNormal");
                int endExportIdx = FindExportIndex1Based("AN_AttackEnd");

                JToken? returnNotify = null;
                foreach (var n in notifiesList) if (GetNotifyName(n) == "AN_ReturnToNormal") returnNotify = n;

                JToken? endNotify = null;
                foreach (var n in notifiesList) if (GetNotifyName(n) == "AN_AttackEnd") endNotify = n;

                if (returnNotify != null)
                {
                    SetLinkValue(returnNotify, cancelTime);
                    SetNotifyObjectRef(returnNotify, returnExportIdx);
                    ResetNotifyStateClass(returnNotify);
                }
                if (endNotify != null)
                {
                    SetLinkValue(endNotify, cancelTime + 0.15f);
                    SetNotifyObjectRef(endNotify, endExportIdx);
                    ResetNotifyStateClass(endNotify);
                }
                // Export自体はあるのにNotify側に無いケースの保険
                if (returnNotify == null)
                {
                    JToken? tmplToken = null;
                    foreach (var n in notifiesList)
                    {
                        if (GetNotifyName(n).StartsWith("AN_")) { tmplToken = n; break; }
                    }
                    if (tmplToken == null) tmplToken = notifiesList.First();

                    var tmpl = tmplToken.DeepClone() as JObject;
                    if (tmpl != null)
                    {
                        SetNotifyName(tmpl, "AN_ReturnToNormal");
                        SetLinkValue(tmpl, cancelTime);
                        SetNotifyObjectRef(tmpl, returnExportIdx);
                        ResetNotifyStateClass(tmpl);
                        notifiesList.Add(tmpl);
                    }
                }
                if (endNotify == null)
                {
                    JToken? tmplToken = null;
                    foreach (var n in notifiesList)
                    {
                        if (GetNotifyName(n).StartsWith("AN_")) { tmplToken = n; break; }
                    }
                    if (tmplToken == null) tmplToken = notifiesList.First();

                    var tmpl = tmplToken.DeepClone() as JObject;
                    if (tmpl != null)
                    {
                        SetNotifyName(tmpl, "AN_AttackEnd");
                        SetLinkValue(tmpl, cancelTime + 0.15f);
                        SetNotifyObjectRef(tmpl, endExportIdx);
                        ResetNotifyStateClass(tmpl);
                        notifiesList.Add(tmpl);
                    }
                }
            }
            File.WriteAllText(jsonPath, root.ToString(Newtonsoft.Json.Formatting.Indented));
        }
    }
}