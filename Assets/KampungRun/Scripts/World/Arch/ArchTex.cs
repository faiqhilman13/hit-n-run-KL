namespace KampungRun.Arch
{
    /// <summary>
    /// The painted detail textures (slices of Resources/KLMap/arch_atlas, a Texture2DArray). The order is
    /// CELLS in Tools/klmap/gen_arch_atlas.py: keep the two in step. [T] slices are painted near-white for
    /// the vertex colour to tint.
    /// </summary>
    public enum ArchTex : byte
    {
        White, Glass, GlassDark, GlassShop, Louvre /*T*/, PanelShutter /*T*/, Roller, Folding /*T*/,
        Door /*T*/, DoorGlass, Breeze /*T*/, Grille, AC, Tiles /*T*/, Zinc /*T*/, Brick /*T*/,
        PlanksH /*T*/, PlanksV /*T*/, Ornament /*T*/, Rail /*T*/, Curtain, Kerawang /*T*/, Vent /*T*/, Concrete /*T*/,
        Signs0, Signs1, Signs2, Signs3, Plaques, Kopitiam, Hardware, Textile,
        GoldShop, Curtained, Lit, AwningRed, AwningGreen, AwningBlue, AwningOrange, Attap /*T*/,
        Posters, Billboard0, Billboard1, Steps,
        WinFar /*T*/, RibbonFar /*T*/, ShopFar,       // distance versions: a storey of windows, a shop front
        Leaves /*T*/, Frond /*T*/, Bark /*T*/, PalmTrunk /*T*/, Banana /*T*/, Bougainvillea, Frangipani,   // plants (Flora.cs)
        Hazard, RoadSigns0, RoadSigns1,                                                                    // bridges (Bridgework.cs)
    }
}
