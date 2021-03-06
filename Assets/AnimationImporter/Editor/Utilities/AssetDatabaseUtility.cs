using UnityEditor;
using UnityEngine;
using System.IO;

namespace AnimationImporter
{
	/// <summary>
	/// Utilities for Unity's built in AssetDatabase class
	/// </summary>
	public static class AssetDatabaseUtility
	{
		public const char UnityDirectorySeparator = '/';
		public const string ResourcesFolderName = "Resources";

		/// <summary>
		/// Creates the asset and any directories that are missing along its path.
		/// </summary>
		/// <param name="unityObject">UnityObject to create an asset for.</param>
		/// <param name="unityFilePath">Unity file path (e.g. "Assets/Resources/MyFile.asset".</param>
		public static void CreateAssetAndDirectories(UnityEngine.Object unityObject, string unityFilePath)
		{
			var pathDirectory = Path.GetDirectoryName(unityFilePath) + UnityDirectorySeparator;
			AssetDatabaseUtility.CreateDirectoriesInPath(pathDirectory);

			AssetDatabase.CreateAsset(unityObject, unityFilePath);
		}

		private static void CreateDirectoriesInPath(string unityDirectoryPath)
		{
			// Check that last character is a directory separator
			if (unityDirectoryPath[unityDirectoryPath.Length - 1] != UnityDirectorySeparator)
			{
				var warningMessage = string.Format(
					                     "Path supplied to CreateDirectoriesInPath that does not include a DirectorySeparator " +
					                     "as the last character." +
					                     "\nSupplied Path: {0}, Filename: {1}",
					                     unityDirectoryPath);
				Debug.LogWarning(warningMessage);
			}

			// Warn and strip filenames
			var filename = Path.GetFileName(unityDirectoryPath);
			if (!string.IsNullOrEmpty(filename))
			{
				var warningMessage = string.Format(
					                     "Path supplied to CreateDirectoriesInPath that appears to include a filename. It will be " +
					                     "stripped. A path that ends with a DirectorySeparate should be supplied. " +
					                     "\nSupplied Path: {0}, Filename: {1}",
					                     unityDirectoryPath,
					                     filename);
				Debug.LogWarning(warningMessage);

				unityDirectoryPath = unityDirectoryPath.Replace(filename, string.Empty);
			}

			var folders = unityDirectoryPath.Split(UnityDirectorySeparator);

			// Error if path does NOT start from Assets
			if (folders.Length > 0 && folders[0] != "Assets")
			{
				var exceptionMessage = "AssetDatabaseUtility CreateDirectoriesInPath expects full Unity path, including 'Assets\\\". " +
				                       "Adding Assets to path.";
				throw new UnityException(exceptionMessage);
			}

			string pathToFolder = string.Empty;
			foreach (var folder in folders)
			{
				// Don't check for or create empty folders
				if (string.IsNullOrEmpty(folder))
				{
					continue;
				}

				// Create folders that don't exist
				pathToFolder = string.Concat(pathToFolder, folder);
				if (!UnityEditor.AssetDatabase.IsValidFolder(pathToFolder))
				{
					var pathToParent = System.IO.Directory.GetParent(pathToFolder).ToString();
					AssetDatabase.CreateFolder(pathToParent, folder);
					AssetDatabase.Refresh();
				}

				pathToFolder = string.Concat(pathToFolder, UnityDirectorySeparator);
			}
		}
	}
}
