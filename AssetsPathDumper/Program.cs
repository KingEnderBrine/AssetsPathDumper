using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AssetsPathDumper
{
	class Program
	{
		public enum FileType { BundleFile, AssetFile, InvalidFile }

		static int Main(string[] args)
		{
			if (args.Length == 0)
            {
				Console.WriteLine("No file/directory specified");
				return -1;
            }

			var path = args[0];
			string[] filePaths;
			if (File.Exists(path))
            {
				filePaths = new[] { path };
            }
			else if (Directory.Exists(path))
            {
				filePaths = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
			}
			else
            {
				Console.WriteLine("Specified file/directory was not found");
				return -1;
			}

			var manager = new AssetsManager();
			manager.LoadClassPackage("classdata.tpk");
			using (var writer = new StreamWriter(new FileStream("assetPathsDump.html", FileMode.Create)))
			{
				foreach (var filePath in filePaths)
				{
				    switch (GetFileType(filePath))
				    {
				        case FileType.BundleFile:
							var fileName = Path.GetFileName(filePath);
							var bundleFileInstance = manager.LoadBundleFile(filePath);
							var paths = ReadAssetBundlePaths(manager, bundleFileInstance);
							
							WriteFileTable(fileName, paths, writer);
							break;
				        case FileType.AssetFile:
							fileName = Path.GetFileName(filePath);
							if (!fileName.Equals("globalgamemanagers", StringComparison.InvariantCultureIgnoreCase))
                            {
								continue;
                            }
							var assetFileInstance = manager.LoadAssetsFile(filePath, false);
							paths = ReadResourcesPaths(manager, assetFileInstance);

							WriteFileTable(fileName, paths, writer);
							break;
				        case FileType.InvalidFile:
							continue;
				    }
				}
			}

			Console.WriteLine("Done");
			return 0;
        }

        private static Dictionary<string, Dictionary<string, int>> ReadAssetBundlePaths(AssetsManager manager, BundleFileInstance bundleFile)
		{
			var paths = new Dictionary<string, Dictionary<string, int>>();
			for (var i = 0; i < bundleFile.file.NumFiles; i++)
			{
				if (!bundleFile.file.IsAssetsFile(i))
                {
					continue;
                }

				var file = manager.LoadAssetsFileFromBundle(bundleFile, i);
				manager.LoadClassDatabaseFromPackage(file.file.typeTree.unityVersion);

				var bundleAssets = file.table.GetAssetsOfType((int)AssetClassID.AssetBundle);
				if (!bundleAssets.Any())
                {
					continue;
                }

				var assetBundleAsset = manager.GetExtAsset(file, 0, bundleAssets.First().index);
				var assetBundleField = assetBundleAsset.instance.GetBaseField();
				var container = assetBundleField.Get("m_Container").children[0];

				foreach (var row in container.children)
				{
					var asset = manager.GetExtAsset(file, row.children[1].Get("asset"), true);
					var typeString = ((AssetClassID)asset.info.curFileType).ToString();
					if ((AssetClassID)asset.info.curFileType == AssetClassID.MonoBehaviour)
					{
						var instance = manager.GetTypeInstance(asset.file, asset.info);
						var scriptAsset = manager.GetExtAsset(asset.file, instance.GetBaseField().Get("m_Script"));
						typeString = scriptAsset.instance.GetBaseField().Get("m_ClassName").GetValue().AsString();
					}
					var types = GetOrCreate(paths, row.children[0].GetValue().AsString(), () => new Dictionary<string, int>());
					var count = GetOrCreate(types, typeString, 0);
					types[typeString] = count + 1;
				}
			}
			return paths;
		}

		private static Dictionary<string, Dictionary<string, int>> ReadResourcesPaths(AssetsManager manager, AssetsFileInstance globalGameManagersFile)
		{
			manager.LoadClassDatabaseFromPackage(globalGameManagersFile.file.typeTree.unityVersion);
			var paths = new Dictionary<string, Dictionary<string, int>>();
			var resourceManagerAsset = manager.GetExtAsset(globalGameManagersFile, 0, globalGameManagersFile.table.GetAssetsOfType((int)AssetClassID.ResourceManager).First().index);
			var resourceManagerField = resourceManagerAsset.instance.GetBaseField();
			var container = resourceManagerField.Get("m_Container").children[0];

			foreach (var row in container.children)
			{
				var asset = manager.GetExtAsset(globalGameManagersFile, row.children[1], true);
				var typeString = ((AssetClassID)asset.info.curFileType).ToString();
				if ((AssetClassID)asset.info.curFileType == AssetClassID.MonoBehaviour)
				{
					var instance = manager.GetTypeInstance(asset.file, asset.info);
					var scriptAsset = manager.GetExtAsset(asset.file, instance.GetBaseField().Get("m_Script"));
					typeString = scriptAsset.instance.GetBaseField().Get("m_ClassName").GetValue().AsString();
				}
				var types = GetOrCreate(paths, row.children[0].GetValue().AsString(), () => new Dictionary<string, int>());
				var count = GetOrCreate(types, typeString, () => 0);
				types[typeString] = count + 1;
			}

			return paths;
		}

		private static void WriteFileTable(string name, Dictionary<string, Dictionary<string, int>> pathsTable, StreamWriter writer)
        {
			writer.WriteLine($"<h1>{name}</h1>");
			writer.WriteLine("<table>");
			writer.WriteLine("	<tr><th>Path</th><th>Available types</th></tr>");
			foreach (var row in pathsTable)
			{
				writer.WriteLine($"	<tr><td>{row.Key}</td><td>{string.Join("<br/>", row.Value.Select(el => $"{el.Key} {(el.Value == 1 ? "" : $"(multiple ({el.Value}))")}"))}</td></tr>");
			}
			writer.WriteLine("</table>");
		}

		private static FileType GetFileType(string filePath)
        {
			string possibleBundleHeader;
			int possibleFormat;
			string emptyVersion;

			using (var fs = File.OpenRead(filePath))
			using (var reader = new AssetsFileReader(fs))
			{
				if (fs.Length < 0x20)
				{
					return FileType.InvalidFile;
				}
				possibleBundleHeader = reader.ReadStringLength(7);
				reader.Position = 0x08;
				possibleFormat = reader.ReadInt32();
				reader.Position = 0x14;

				var possibleVersion = "";
				char curChar;
				while (reader.Position < reader.BaseStream.Length && (curChar = (char)reader.ReadByte()) != 0x00)
				{
					possibleVersion += curChar;
					if (possibleVersion.Length < 0xFF)
					{
						break;
					}
				}
				emptyVersion = Regex.Replace(possibleVersion, "[a-zA-Z0-9\\.]", "");
			}

			if (possibleBundleHeader == "UnityFS")
			{
				return FileType.BundleFile;
			}
			else if (possibleFormat < 0xFF && emptyVersion == "")
			{
				return FileType.AssetFile;
			}
			else
			{
				return FileType.InvalidFile;
			}
		}

		private static TValue GetOrCreate<TKey, TValue>(Dictionary<TKey, TValue> dict, TKey key, TValue defaultValue) where TKey : notnull
		{
			return dict.TryGetValue(key, out var value) ? value : (dict[key] = defaultValue);
		}

		private static TValue GetOrCreate<TKey, TValue>(Dictionary<TKey, TValue> dict, TKey key, Func<TValue> defaultValueFunc) where TKey : notnull
        {
            return dict.TryGetValue(key, out var value) ? value : (dict[key] = defaultValueFunc());
        }
    }
}