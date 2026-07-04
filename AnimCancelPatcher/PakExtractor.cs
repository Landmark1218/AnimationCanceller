using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AnimCancelPatcher
{
    public class PakExtractor
    {
        public async Task<string> ExtractAssetAsync(string pakDir, string aesKeyHex, string relativeAssetDir, string assetName)
        {
            await ZlibHelper.InitializeAsync();

            bool iSEXtracting = true;
            long fileSize = 0;

            return await Task.Run(() =>
            {
                var provider = new DefaultFileProvider(pakDir, SearchOption.AllDirectories, new VersionContainer(EGame.GAME_UE4_27));
                provider.Initialize();

                string key = aesKeyHex;
                if (!key.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    key = "0x" + aesKeyHex;
                }
                provider.SubmitKey(new FGuid(), new FAesKey(key));

                string virtualUasset = $"{relativeAssetDir}/{assetName}.uasset";
                string virtualUexp = $"{relativeAssetDir}/{assetName}.uexp";

                string outputBaseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");

                string targetOutDir = Path.Combine(outputBaseDir, relativeAssetDir.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(targetOutDir))
                {
                    Directory.CreateDirectory(targetOutDir);
                }

                string outUasset = Path.Combine(targetOutDir, $"{assetName}.uasset");
                string outUexp = Path.Combine(targetOutDir, $"{assetName}.uexp");

                if (provider.Files.TryGetValue(virtualUasset, out var gfUasset))
                {
                    byte[] data = gfUasset.Read();
                    using (var fs = new FileStream(outUasset, FileMode.Create, FileAccess.Write))
                    {
                        fs.Write(data, 0, data.Length);
                    }
                }
                else
                {
                    throw new FileNotFoundException($"Pak内にファイルが見つかりません: {virtualUasset}");
                }

                if (provider.Files.TryGetValue(virtualUexp, out var gfUexp))
                {
                    byte[] data = gfUexp.Read();
                    using (var fs = new FileStream(outUexp, FileMode.Create, FileAccess.Write))
                    {
                        fs.Write(data, 0, data.Length);
                    }
                }
                else
                {
                    throw new FileNotFoundException($"Pak内にファイルが見つかりません: {virtualUexp}");
                }

                return targetOutDir;
            });
        }
    }
}