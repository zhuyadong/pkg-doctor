using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AssetStudio
{
    public enum FileType
    {
        AssetsFile,
        BundleFile,
        WebFile,
        ResourceFile
    }

    public static class ImportHelper
    {
        public static void MergeSplitAssets(string path, bool allDirectories = false)
        {
            var splitFiles = Directory.GetFiles(path, "*.split0", allDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            foreach (var splitFile in splitFiles)
            {
                var destFile = Path.GetFileNameWithoutExtension(splitFile);
                var destPath = Path.GetDirectoryName(splitFile);
                var destFull = Path.Combine(destPath, destFile);
                if (!File.Exists(destFull))
                {
                    var splitParts = Directory.GetFiles(destPath, destFile + ".split*");
                    using (var destStream = File.Create(destFull))
                    {
                        for (int i = 0; i < splitParts.Length; i++)
                        {
                            var splitPart = destFull + ".split" + i;
                            using (var sourceStream = File.OpenRead(splitPart))
                            {
                                sourceStream.CopyTo(destStream);
                            }
                        }
                    }
                }
            }
        }

        public static string[] ProcessingSplitFiles(List<string> selectFile)
        {
            var splitFiles = selectFile.Where(x => x.Contains(".split"))
                .Select(x => Path.Combine(Path.GetDirectoryName(x), Path.GetFileNameWithoutExtension(x)))
                .Distinct()
                .ToList();
            selectFile.RemoveAll(x => x.Contains(".split"));
            foreach (var file in splitFiles)
            {
                if (File.Exists(file))
                {
                    selectFile.Add(file);
                }
            }
            return selectFile.Distinct().ToArray();
        }

        public static FileType CheckFileType(Stream stream, out EndianBinaryReader reader)
        {
            if (IsZstd(stream))
            {
                var decompressor = new ZstdNet.Decompressor();
                var data = decompressor.Unwrap(GetBytes(stream));
                stream.Dispose();
                stream = new MemoryStream(data, false);
            }
            reader = new EndianBinaryReader(stream);
            return CheckFileType(reader);
        }

        public static FileType CheckFileType(string fileName, out EndianBinaryReader reader)
        {
            var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return CheckFileType(stream, out reader);
        }

        private static readonly byte[] zstdMagic = { 0x28, 0xB5, 0x2F, 0xFD };
        private static bool IsZstd(Stream stream)
        {
            var magic = new byte[zstdMagic.Length];
            stream.Read(magic, 0, magic.Length);
            stream.Seek(0, SeekOrigin.Begin);
            return zstdMagic.SequenceEqual(magic);
        }
        
        private static byte[] GetBytes(Stream stream)
        {
            int len = (int)stream.Length;
            int pos = 0;

            var bytes = new byte[len];

            while (len > 0)
            {
                int n = stream.Read(bytes, pos, len);
                if (n == 0)
                    break;
                pos += n;
                len -= n;
            }
            return bytes;
        }
        
        private static FileType CheckFileType(EndianBinaryReader reader)
        {
            var signature = reader.ReadStringToNull(20);
            if (signature == "")
            {
                var bytes = reader.ReadBytes(20);

            }
            reader.Position = 0;
            switch (signature)
            {
                case "UnityWeb":
                case "UnityRaw":
                case "UnityArchive":
                case "UnityFS":
                    return FileType.BundleFile;
                case "UnityWebData1.0":
                    return FileType.WebFile;
                default:
                    {
                        var magic = reader.ReadBytes(2);
                        reader.Position = 0;
                        if (WebFile.gzipMagic.SequenceEqual(magic))
                        {
                            return FileType.WebFile;
                        }
                        reader.Position = 0x20;
                        magic = reader.ReadBytes(6);
                        reader.Position = 0;
                        if (WebFile.brotliMagic.SequenceEqual(magic))
                        {
                            return FileType.WebFile;
                        }
                        if (SerializedFile.IsSerializedFile(reader))
                        {
                            return FileType.AssetsFile;
                        }
                        else
                        {
                            if (signature.EndsWith("FS"))
                                return FileType.BundleFile;

                            if (reader.BaseStream.Length > 16)
                            {
                                int[] offsets = { 11, 16 };
                                foreach (var offset in offsets)
                                {
                                    reader.Position = offset;
                                    signature = reader.ReadStringToNull(20);
                                    switch (signature)
                                    {
                                        case "UnityWeb":
                                        case "UnityRaw":
                                        case "UnityArchive":
                                        case "UnityFS":
                                            return FileType.BundleFile;
                                        case "UnityWebData1.0":
                                            return FileType.WebFile;
                                    }
                                }
                            }
                            return FileType.ResourceFile;
                        }
                    }
            }
        }
    }
}
