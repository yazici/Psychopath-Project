using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;
using UnityEditor;
using System.IO;
using AnimationImporter.Boomlagoon.JSON;
using UnityEditor.Animations;
using System.Linq;

namespace AnimationImporter
{
	public class AnimationImporter
	{
		// ================================================================================
		//	Singleton
		// --------------------------------------------------------------------------------

		private static AnimationImporter _instance = null;
		public static AnimationImporter Instance
		{
			get
			{
				if (_instance == null)
				{
					_instance = new AnimationImporter();
				}

				return _instance;
			}
		}

		// ================================================================================
		//  delegates
		// --------------------------------------------------------------------------------

		public delegate bool HasCustomReImportDelegate(string fileName);
		public static HasCustomReImportDelegate HasCustomReImport = null;

		// ================================================================================
		//  const
		// --------------------------------------------------------------------------------

		private const string PREFS_PREFIX = "ANIMATION_IMPORTER_";
		private const string SHARED_CONFIG_PATH = "Assets/Resources/AnimationImporter/AnimationImporterConfig.asset";

		private static string[] allowedExtensions = { "ase", "aseprite" };

		// ================================================================================
		//  user values
		// --------------------------------------------------------------------------------

		string _asepritePath = "";
		public string asepritePath
		{
			get
			{
				return _asepritePath;
			}
			set
			{
				if (_asepritePath != value)
				{
					_asepritePath = value;
					SaveUserConfig();
					CheckIfApplicationIsValid();
				}
			}
		}

		private RuntimeAnimatorController _baseController = null;
		public RuntimeAnimatorController baseController
		{
			get
			{
				return _baseController;
			}
			set
			{
				if (_baseController != value)
				{
					_baseController = value;
					SaveUserConfig();
				}
			}
		}

		private AnimationImporterSharedConfig _sharedData;
		public AnimationImporterSharedConfig sharedData
		{
			get
			{
				return _sharedData;
			}
		}

		// ================================================================================
		//  private
		// --------------------------------------------------------------------------------

		private bool _hasApplication = false;
		public bool canImportAnimations
		{
			get
			{
				return _hasApplication;
			}
		}
		public bool canImportAnimationsForOverrideController
		{
			get
			{
				return canImportAnimations && _baseController != null;
			}
		}

		// ================================================================================
		//  save and load user values
		// --------------------------------------------------------------------------------

		public void LoadOrCreateUserConfig()
		{
			LoadPreferences();

			_sharedData = ScriptableObjectUtility.LoadOrCreateSaveData<AnimationImporterSharedConfig>(SHARED_CONFIG_PATH);
		}

		public void LoadUserConfig()
		{
			LoadPreferences();

			_sharedData = ScriptableObjectUtility.LoadSaveData<AnimationImporterSharedConfig>(SHARED_CONFIG_PATH);
		}

		private void LoadPreferences()
		{
			if (PlayerPrefs.HasKey(PREFS_PREFIX + "asepritePath"))
			{
				_asepritePath = PlayerPrefs.GetString(PREFS_PREFIX + "asepritePath");
			}
			else
			{
				_asepritePath = AsepriteImporter.standardApplicationPath;

				if (!File.Exists(_asepritePath))
					_asepritePath = "";
			}

			if (PlayerPrefs.HasKey(PREFS_PREFIX + "baseControllerPath"))
			{
				string baseControllerPath = PlayerPrefs.GetString(PREFS_PREFIX + "baseControllerPath");
				if (!string.IsNullOrEmpty(baseControllerPath))
				{
					_baseController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(baseControllerPath);
				}
			}

			CheckIfApplicationIsValid();
		}

		private void SaveUserConfig()
		{
			PlayerPrefs.SetString(PREFS_PREFIX + "asepritePath", _asepritePath);

			if (_baseController != null)
			{
				PlayerPrefs.SetString(PREFS_PREFIX + "baseControllerPath", AssetDatabase.GetAssetPath(_baseController));
			}
			else
			{
				PlayerPrefs.SetString(PREFS_PREFIX + "baseControllerPath", "");
			}
		}

		// ================================================================================
		//  import methods
		// --------------------------------------------------------------------------------

		public ImportedAnimationSheet CreateAnimationsForAssetFile(DefaultAsset droppedAsset)
		{
			return CreateAnimationsForAssetFile(AssetDatabase.GetAssetPath(droppedAsset));
		}

		public ImportedAnimationSheet CreateAnimationsForAssetFile(string assetPath, string additionalCommandLineArguments = null)
		{
			if (!IsValidAsset(assetPath))
			{
				return null;
			}

			string fileName = Path.GetFileName(assetPath);
			string assetName = Path.GetFileNameWithoutExtension(fileName);
			string basePath = GetBasePath(assetPath);

			// we analyze import settings on existing files
			PreviousImportSettings previousAnimationInfo = CollectPreviousImportSettings(basePath, assetName);

			if (AsepriteImporter.CreateSpriteAtlasAndMetaFile(_asepritePath, additionalCommandLineArguments, basePath, fileName, assetName, _sharedData.saveSpritesToSubfolder))
			{
				AssetDatabase.Refresh();
				return ImportJSONAndCreateAnimations(basePath, assetName, previousAnimationInfo);
			}

			return null;
		}

		public void CreateAnimatorController(ImportedAnimationSheet animations)
		{
			AnimatorController controller;

			// check if controller already exists; use this to not loose any references to this in other assets
			string pathForAnimatorController = animations.basePath + "/" + animations.name + ".controller";
			controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(pathForAnimatorController);

			if (controller == null)
			{
				// create a new controller and place every animation as a state on the first layer
				controller = AnimatorController.CreateAnimatorControllerAtPath(animations.basePath + "/" + animations.name + ".controller");
				controller.AddLayer("Default");

				foreach (var animation in animations.animations)
				{
					AnimatorState state = controller.layers[0].stateMachine.AddState(animation.name);
					state.motion = animation.animationClip;
				}
			}
			else
			{
				// look at all states on the first layer and replace clip if state has the same name
				var childStates = controller.layers[0].stateMachine.states;
				foreach (var childState in childStates)
				{
					AnimationClip clip = animations.GetClip(childState.state.name);
					if (clip != null)
						childState.state.motion = clip;
				}
			}

			EditorUtility.SetDirty(controller);
			AssetDatabase.SaveAssets();
		}

		public void CreateAnimatorOverrideController(ImportedAnimationSheet animations, bool useExistingBaseController = false)
		{
			AnimatorOverrideController overrideController;

			// check if override controller already exists; use this to not loose any references to this in other assets
			string pathForOverrideController = animations.basePath + "/" + animations.name + ".overrideController";
			overrideController = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(pathForOverrideController);

			RuntimeAnimatorController baseController = _baseController;
			if (useExistingBaseController && overrideController.runtimeAnimatorController != null)
			{
				baseController = overrideController.runtimeAnimatorController;
			}

			if (baseController != null)
			{
				if (overrideController == null)
				{
					overrideController = new AnimatorOverrideController();
					AssetDatabase.CreateAsset(overrideController, pathForOverrideController);
				}

				overrideController.runtimeAnimatorController = baseController;

				// set override clips
				var clipPairs = overrideController.clips;
				for (int i = 0; i < clipPairs.Length; i++)
				{
					string animationName = clipPairs[i].originalClip.name;
					AnimationClip clip = animations.GetClipOrSimilar(animationName);
					clipPairs[i].overrideClip = clip;
				}
				overrideController.clips = clipPairs;

				EditorUtility.SetDirty(overrideController);
			}
			else
			{
				Debug.LogWarning("No Animator Controller found as a base for the Override Controller");
			}
		}

		public ImportedAnimationSheet ImportSpritesAndAnimationSheet(string assetPath, string additionalCommandLineArguments = null)
		{
			if (assetPath == null)
			{
				return null;
			}

			if (sharedData == null)
			{
				LoadUserConfig();
			}

			string fileName = Path.GetFileName(assetPath);
			string assetName = Path.GetFileNameWithoutExtension(fileName);
			string basePath = GetBasePath(assetPath);

			// we analyze import settings on existing files
			PreviousImportSettings previousAnimationInfo = CollectPreviousImportSettings(basePath, assetName);

			if (AsepriteImporter.CreateSpriteAtlasAndMetaFile(_asepritePath, additionalCommandLineArguments, basePath, fileName, assetName, _sharedData.saveSpritesToSubfolder))
			{
				AssetDatabase.Refresh();
				return ImportJSONAndCreateSprites(basePath, assetName, previousAnimationInfo);
			}

			return null;
		}

		// ================================================================================
		//  import images and create animations
		// --------------------------------------------------------------------------------

		private ImportedAnimationSheet ImportJSONAndCreateAnimations(string basePath, string name, PreviousImportSettings previousImportSettings)
		{
			// parse the JSON file
			ImportedAnimationSheet animationSheet = CreateAnimationSheetFromJSON(basePath, name, previousImportSettings);

			CreateSprites(animationSheet);
			CreateAnimations(animationSheet);

			return animationSheet;
		}

		private ImportedAnimationSheet ImportJSONAndCreateSprites(string basePath, string name, PreviousImportSettings previousImportSettings)
		{
			// parse the JSON file
			ImportedAnimationSheet animationSheet = CreateAnimationSheetFromJSON(basePath, name, previousImportSettings);

			CreateSprites(animationSheet);

			return animationSheet;
		}

		// parses a JSON file and creates the raw data for ImportedAnimationSheet from it
		private ImportedAnimationSheet CreateAnimationSheetFromJSON(string basePath, string name, PreviousImportSettings previousImportSettings)
		{
			string textAssetFilename = GetJSONAssetFilename(basePath, name);
			TextAsset textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(textAssetFilename);

			if (textAsset != null)
			{
				JSONObject jsonObject = JSONObject.Parse(textAsset.ToString());
				ImportedAnimationSheet animationSheet = AsepriteImporter.GetAnimationInfo(jsonObject);

				if (animationSheet == null)
					return null;

				animationSheet.previousImportSettings = previousImportSettings;

				animationSheet.name = name;
				animationSheet.basePath = basePath;

				animationSheet.SetNonLoopingAnimations(sharedData.animationNamesThatDoNotLoop);

				// delete JSON file afterwards
				AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(textAsset));

				return animationSheet;
			}
			else
			{
				Debug.LogWarning("Problem with JSON file: " + textAssetFilename);
			}

			return null;
		}

		private void CreateAnimations(ImportedAnimationSheet animationSheet)
		{
			string imageAssetFilename = GetImageAssetFilename(animationSheet.basePath, animationSheet.name);

			if (animationSheet.hasAnimations)
			{
				if (sharedData.saveAnimationsToSubfolder)
				{
					string path = animationSheet.basePath + "/Animations";
					if (!Directory.Exists(path))
					{
						Directory.CreateDirectory(path);
					}

					CreateAnimationAssets(animationSheet, imageAssetFilename, path);
				}
				else
				{
					CreateAnimationAssets(animationSheet, imageAssetFilename, animationSheet.basePath);
				}
			}
		}

		private void CreateAnimationAssets(ImportedAnimationSheet animationInfo, string imageAssetFilename, string pathForAnimations)
		{
			string masterName = Path.GetFileNameWithoutExtension(imageAssetFilename);

			foreach (var animation in animationInfo.animations)
			{
				animationInfo.CreateAnimation(animation, pathForAnimations, masterName, sharedData.targetObjectType);
			}
		}

		private void CreateSprites(ImportedAnimationSheet animationSheet)
		{
			string imageFile = GetImageAssetFilename(animationSheet.basePath, animationSheet.name);

			TextureImporter importer = AssetImporter.GetAtPath(imageFile) as TextureImporter;

			// apply texture import settings if there are no previous ones
			if (!animationSheet.hasPreviousTextureImportSettings)
			{
				importer.textureType = TextureImporterType.Sprite;
				importer.spritePixelsPerUnit = sharedData.spritePixelsPerUnit;
				importer.mipmapEnabled = false;
				importer.filterMode = FilterMode.Point;
				importer.textureFormat = TextureImporterFormat.AutomaticTruecolor;
			}

			// create sub sprites for this file according to the AsepriteAnimationInfo 
			importer.spritesheet = animationSheet.GetSpriteSheet(
				sharedData.spriteAlignment,
				sharedData.spriteAlignmentCustomX,
				sharedData.spriteAlignmentCustomY);

			// reapply old import settings (pivot settings for sprites)
			if (animationSheet.hasPreviousTextureImportSettings)
			{
				animationSheet.previousImportSettings.ApplyPreviousTextureImportSettings(importer);
			}

			// these values will be set in any case, not influenced by previous import settings
			importer.spriteImportMode = SpriteImportMode.Multiple;
			importer.maxTextureSize = animationSheet.maxTextureSize;

			EditorUtility.SetDirty(importer);

			try
			{
				importer.SaveAndReimport();
			}
			catch (Exception e)
			{
				Debug.LogWarning("There was a problem with applying settings to the generated sprite file: " + e.ToString());
			}

			AssetDatabase.ImportAsset(imageFile, ImportAssetOptions.ForceUpdate);

			Sprite[] createdSprites = GetAllSpritesFromAssetFile(imageFile);
			animationSheet.ApplyCreatedSprites(createdSprites);
		}

		private static Sprite[] GetAllSpritesFromAssetFile(string imageFilename)
		{
			var assets = AssetDatabase.LoadAllAssetsAtPath(imageFilename);
			List<Sprite> sprites = new List<Sprite>();
			foreach (var item in assets)
			{
				if (item is Sprite)
				{
					sprites.Add(item as Sprite);
				}
			}

			// we order the sprites by name here because the LoadAllAssets above does not necessarily return the sprites in correct order
			// the OrderBy is fed with the last word of the name, which is an int from 0 upwards
			Sprite[] orderedSprites = sprites
									 .OrderBy(x => int.Parse(x.name.Substring(x.name.LastIndexOf(' ')).TrimStart()))
									 .ToArray();

			return orderedSprites;
		}

		// ================================================================================
		//  querying existing assets
		// --------------------------------------------------------------------------------

		// check if this is a valid file; we are only looking at the file extension here
		public static bool IsValidAsset(string path)
		{
			string extension = Path.GetExtension(path);

			for (int i = 0; i < allowedExtensions.Length; i++)
			{
				if (extension == "." + allowedExtensions[i])
				{
					return true;
				}
			}

			return false;
		}

		public bool HasExistingRuntimeAnimatorController(string assetPath)
		{
			return HasExistingAnimatorController(assetPath) || HasExistingAnimatorOverrideController(assetPath);
		}

		public bool HasExistingAnimatorController(string assetPath)
		{
			return GetExistingAnimatorController(assetPath) != null;
		}

		public bool HasExistingAnimatorOverrideController(string assetPath)
		{
			return GetExistingAnimatorOverrideController(assetPath) != null;
		}

		public RuntimeAnimatorController GetExistingRuntimeAnimatorController(string assetPath)
		{
			AnimatorController animatorController = GetExistingAnimatorController(assetPath);
			if (animatorController != null)
			{
				return animatorController;
			}

			return GetExistingAnimatorOverrideController(assetPath);
		}

		public AnimatorController GetExistingAnimatorController(string assetPath)
		{
			string name = Path.GetFileNameWithoutExtension(assetPath);
			string basePath = GetBasePath(assetPath);

			string pathForController = basePath + "/" + name + ".controller";
			AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(pathForController);

			return controller;
		}

		public AnimatorOverrideController GetExistingAnimatorOverrideController(string assetPath)
		{
			string name = Path.GetFileNameWithoutExtension(assetPath);
			string basePath = GetBasePath(assetPath);

			string pathForController = basePath + "/" + name + ".overrideController";
			AnimatorOverrideController controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(pathForController);

			return controller;
		}

		// ================================================================================
		//  automatic ReImport
		// --------------------------------------------------------------------------------

		/// <summary>
		/// will be called by the AssetPostProcessor
		/// </summary>
		public void AutomaticReImport(string filePath)
		{
			if (filePath == null)
			{
				return;
			}

			// check if file is handled by other Importers
			if (HasCustomReImport != null && HasCustomReImport(filePath))
			{
				return;
			}

			HandleReImport(filePath);
		}

		/// <summary>
		/// can be used for manually handling ReImport
		/// </summary>
		public void HandleReImport(string filePath, string additionalCommandLineArguments = null)
		{
			if (filePath == null)
			{
				return;
			}

			if (sharedData == null)
			{
				LoadUserConfig();
			}

			if (HasExistingAnimatorController(filePath))
			{
				var animationInfo = CreateAnimationsForAssetFile(filePath, additionalCommandLineArguments);

				if (animationInfo != null)
				{
					CreateAnimatorController(animationInfo);
				}
			}
			else if (HasExistingAnimatorOverrideController(filePath))
			{
				var animationInfo = CreateAnimationsForAssetFile(filePath, additionalCommandLineArguments);

				if (animationInfo != null)
				{
					CreateAnimatorOverrideController(animationInfo, true);
				}
			}
		}

		// ================================================================================
		//  private methods
		// --------------------------------------------------------------------------------

		private PreviousImportSettings CollectPreviousImportSettings(string basePath, string name)
		{
			PreviousImportSettings previousImportSettings = new PreviousImportSettings();

			previousImportSettings.GetTextureImportSettings(GetImageAssetFilename(basePath, name));

			return previousImportSettings;
		}

		private void CheckIfApplicationIsValid()
		{
			_hasApplication = File.Exists(_asepritePath);
		}

		private string GetBasePath(string path)
		{
			string extension = Path.GetExtension(path);
			if (extension.Length > 0 && extension[0] == '.')
			{
				extension = extension.Remove(0, 1);
			}

			string fileName = Path.GetFileNameWithoutExtension(path);
			string lastPart = "/" + fileName + "." + extension;

			return path.Replace(lastPart, "");
		}

		private string GetImageAssetFilename(string basePath, string name)
		{
			if (sharedData.saveSpritesToSubfolder)
				basePath += "/Sprites";

			return basePath + "/" + name + ".png";
		}

		private string GetJSONAssetFilename(string basePath, string name)
		{
			if (sharedData.saveSpritesToSubfolder)
				basePath += "/Sprites";

			return basePath + "/" + name + ".json";
		}
	}
}
