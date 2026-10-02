namespace ALSUnity.EditorTools
{
    /// <summary>Asset locations shared by the ALS editor build steps.</summary>
    public static class ALSAssetPaths
    {
        public const string Root = "Assets/ALS";
        public const string Ual1Model = Root + "/Art/Quaternius/Mannequin_UAL1.fbx";
        public const string Ual1BackwardModel = Root + "/Art/Quaternius/Locomotion_UAL1_Backward.fbx";
        public const string Ual2Model = Root + "/Art/Quaternius/Actions_UAL2.fbx";
        public const string M2MAddonModel = Root + "/Art/Mesh2Motion/M2M_Addon.fbx";
        public const string M2MMocapModel = Root + "/Art/Mesh2Motion/M2M_Mocap.fbx";
        public const string MaterialsFolder = Root + "/Art/Materials";
        public const string TexturesFolder = Root + "/Art/Textures";
        public const string AnimationFolder = Root + "/Animation";
        public const string AnimatorController = AnimationFolder + "/ALSCharacter.controller";
        public const string PrefabsFolder = Root + "/Prefabs";
        public const string CharacterPrefab = PrefabsFolder + "/ALSCharacter.prefab";
        public const string ScenesFolder = Root + "/Scenes";
        public const string TestScene = ScenesFolder + "/ALS_TestScene.unity";
    }
}
