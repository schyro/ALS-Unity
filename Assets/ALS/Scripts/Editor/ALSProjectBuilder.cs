using UnityEditor;

namespace ALSUnity.EditorTools
{
    /// <summary>Runs every build step: model import settings, animator, character prefab and test scene.</summary>
    public static class ALSProjectBuilder
    {
        [MenuItem("ALS/Rebuild Everything")]
        public static void BuildAll()
        {
            ALSModelImportSetup.ConfigureImports();
            ALSAnimatorBuilder.Build();
            ALSCharacterPrefabBuilder.Build();
            ALSSceneBuilder.Build();
        }
    }
}
