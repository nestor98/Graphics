namespace UnityEngine.Rendering.HighDefinition
{
    // internal class UWWaterConsts
    // {
    //     // The maximal number of bands that the system can hold
    //     public const int k_nWavelengths = 1024;
    // }
    // static const int k_nWavelengths = 2056;

    [GenerateHLSL(needAccessors = false, generateCBuffer = true)]
    unsafe struct UWRGBShaderVariablesUnderWater
    {
        // Followed this: https://github.com/Unity-Technologies/Graphics/blob/f74d58f39fa78d5457081b6dbfd43887a153076a/Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesGlobal.cs
        // And its advice on using padded Vector4s and NEVER Vector3 (padding is API dependant, OpenGL!=Vulkan, etc.)

        // [Scattering, extinction, Diff Downwelling, -]
        // [HLSLArray(UWWaterConsts.k_nWavelengths, typeof(Vector4))]
        // public fixed float _WaterScatExtDw[UWWaterConsts.k_nWavelengths*4]; // 8 wls * 4 values
        // // Camera response curve:
        // [HLSLArray(UWWaterConsts.k_nWavelengths, typeof(Vector4))]
        // public fixed float _ResponseCurve[UWWaterConsts.k_nWavelengths*4]; // [R,G,B,-]

        public Vector4 _Scat, _Ext, _Dw;

        // public uint _NWavelengths; // Always 8

        public float _CamToSurface;

    }
}
