using UnityEngine;
using UnityEditor;
using System;


namespace Return.Editors
{
    /// <summary>
    /// Archive export config as preference.
    /// </summary>
    [Serializable]
    public class ExportBundle
    {
        /// <summary>
        /// Index of json data.
        /// </summary>
        public const string confirm = "$ExportBundle";

        /// <summary>
        /// Display name.
        /// </summary>
        public string Name;
        /// <summary>
        /// Bundle packaging approach.
        /// </summary>
        public ExportPackageOptions PackageOption;
        /// <summary>
        /// Extra bundle data works with unity project. 
        /// </summary>
        public ExportProjectOption ProjectOption;
        /// <summary>
        /// Bundle data persistent path.
        /// </summary>
        public string FilePath;
        /// <summary>
        ///  Bundle data persistent name.
        /// </summary>
        public string FileName;
        /// <summary>
        /// Unity editor asset GUID inside this bundle.
        /// </summary>
        public string[] GUIDs;

        public bool IsCreateDummyPrefab;
        public bool BindingAsVariant;
        public string VariantFolderPath;

        /// <summary>
        /// Convert json context as export bundle.
        /// </summary>
        public static ExportBundle LoadJson(string json)
        {
            //Debug.Log($"Contains " + json.Contains(nameof(PackageOption)));

            if (json.StartsWith(confirm))
                json = json[confirm.Length..];
            else if (!json.Contains(nameof(PackageOption)))
                return null;


            //Debug.Log($"name : {config.name} type : {config.GetType()}");
            var bundle = new ExportBundle();

            if (!JsonUtil.LoadJson(json, bundle))
                return null;

            if (string.IsNullOrEmpty(bundle.Name))
            {
                if(bundle.GUIDs==null || bundle.GUIDs.Length==0)
                    return null;

                bundle.Name = "Unknow";
            }

            return bundle;
        }

    }
}