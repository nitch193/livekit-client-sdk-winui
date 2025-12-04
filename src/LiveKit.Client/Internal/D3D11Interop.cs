using System;
using System.Runtime.InteropServices;

namespace LiveKit.Internal
{
    internal static class D3D11Interop
    {
        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow(
                [In] IntPtr window,
                [In] ref Guid iid);

            IntPtr CreateForMonitor(
                [In] IntPtr monitor,
                [In] ref Guid iid);
        }

        [ComImport]
        [Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IInitializeWithWindow
        {
            void Initialize(IntPtr hwnd);
        }

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        public const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

        [ComImport]
        [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDirect3DDxgiInterfaceAccess
        {
            IntPtr GetInterface([In] ref Guid iid);
        }


        [ComImport]
        [Guid("db6f6ddb-ac77-4e88-8253-819df9bbf140")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ID3D11Device
        {
            void CreateBuffer(); // Placeholder
            void CreateTexture1D(); // Placeholder
            void CreateTexture2D(
                [In] ref D3D11_TEXTURE2D_DESC pDesc,
                [In] IntPtr pInitialData,
                [Out] out ID3D11Texture2D ppTexture2D);
            void CreateTexture3D(); // Placeholder
            void CreateShaderResourceView(); // Placeholder
            void CreateUnorderedAccessView(); // Placeholder
            void CreateRenderTargetView(); // Placeholder
            void CreateDepthStencilView(); // Placeholder
            void CreateInputLayout(); // Placeholder
            void CreateVertexShader(); // Placeholder
            void CreateGeometryShader(); // Placeholder
            void CreateGeometryShaderWithStreamOutput(); // Placeholder
            void CreatePixelShader(); // Placeholder
            void CreateHullShader(); // Placeholder
            void CreateDomainShader(); // Placeholder
            void CreateComputeShader(); // Placeholder
            void CreateClassLinkage(); // Placeholder
            void CreateBlendState(); // Placeholder
            void CreateDepthStencilState(); // Placeholder
            void CreateRasterizerState(); // Placeholder
            void CreateSamplerState(); // Placeholder
            void CreateQuery(); // Placeholder
            void CreatePredicate(); // Placeholder
            void CreateCounter(); // Placeholder
            void CreateDeferredContext(); // Placeholder
            void OpenSharedResource(); // Placeholder
            void CheckFormatSupport(); // Placeholder
            void CheckMultisampleQualityLevels(); // Placeholder
            void CheckCounterInfo(); // Placeholder
            void CheckCounter(); // Placeholder
            void CheckFeatureSupport(); // Placeholder
            void GetPrivateData(); // Placeholder
            void SetPrivateData(); // Placeholder
            void SetPrivateDataInterface(); // Placeholder
            void GetFeatureLevel(); // Placeholder
            void GetCreationFlags(); // Placeholder
            void GetDeviceRemovedReason(); // Placeholder
            void GetImmediateContext([Out] out ID3D11DeviceContext ppImmediateContext);
        }

        [ComImport]
        [Guid("c0bfa96c-e089-44fb-8eaf-26f8796190da")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ID3D11DeviceContext
        {
            void VSSetConstantBuffers(); // Placeholder
            void PSSetShaderResources(); // Placeholder
            void PSSetShader(); // Placeholder
            void PSSetSamplers(); // Placeholder
            void VSSetShader(); // Placeholder
            void DrawIndexed(); // Placeholder
            void Draw(); // Placeholder
            void Map(
                [In] ID3D11Resource pResource,
                [In] uint Subresource,
                [In] D3D11_MAP MapType,
                [In] uint MapFlags,
                [Out] out D3D11_MAPPED_SUBRESOURCE pMappedResource);
            void Unmap(
                [In] ID3D11Resource pResource,
                [In] uint Subresource);
            void PSSetConstantBuffers(); // Placeholder
            void IASetInputLayout(); // Placeholder
            void IASetVertexBuffers(); // Placeholder
            void IASetIndexBuffer(); // Placeholder
            void DrawIndexedInstanced(); // Placeholder
            void DrawInstanced(); // Placeholder
            void GSSetConstantBuffers(); // Placeholder
            void GSSetShader(); // Placeholder
            void IASetPrimitiveTopology(); // Placeholder
            void VSSetShaderResources(); // Placeholder
            void VSSetSamplers(); // Placeholder
            void Begin(); // Placeholder
            void End(); // Placeholder
            void GetData(); // Placeholder
            void SetPredication(); // Placeholder
            void GSSetShaderResources(); // Placeholder
            void GSSetSamplers(); // Placeholder
            void OMSetRenderTargets(); // Placeholder
            void OMSetRenderTargetsAndUnorderedAccessViews(); // Placeholder
            void OMSetBlendState(); // Placeholder
            void OMSetDepthStencilState(); // Placeholder
            void SOSetTargets(); // Placeholder
            void DrawAuto(); // Placeholder
            void DrawIndexedInstancedIndirect(); // Placeholder
            void DrawInstancedIndirect(); // Placeholder
            void Dispatch(); // Placeholder
            void DispatchIndirect(); // Placeholder
            void RSSetState(); // Placeholder
            void RSSetViewports(); // Placeholder
            void RSSetScissorRects(); // Placeholder
            void CopySubresourceRegion(); // Placeholder
            void CopyResource(
                [In] ID3D11Resource pDstResource,
                [In] ID3D11Resource pSrcResource);
            void UpdateSubresource(); // Placeholder
            void CopyStructureCount(); // Placeholder
            void ClearRenderTargetView(); // Placeholder
            void ClearUnorderedAccessViewUint(); // Placeholder
            void ClearUnorderedAccessViewFloat(); // Placeholder
            void ClearDepthStencilView(); // Placeholder
            void GenerateMips(); // Placeholder
            void SetResourceMinLOD(); // Placeholder
            void GetResourceMinLOD(); // Placeholder
            void ResolveSubresource(); // Placeholder
            void ExecuteCommandList(); // Placeholder
            void HSSetShaderResources(); // Placeholder
            void HSSetShader(); // Placeholder
            void HSSetSamplers(); // Placeholder
            void HSSetConstantBuffers(); // Placeholder
            void DSSetShaderResources(); // Placeholder
            void DSSetShader(); // Placeholder
            void DSSetSamplers(); // Placeholder
            void DSSetConstantBuffers(); // Placeholder
            void CSSetShaderResources(); // Placeholder
            void CSSetUnorderedAccessViews(); // Placeholder
            void CSSetShader(); // Placeholder
            void CSSetSamplers(); // Placeholder
            void CSSetConstantBuffers(); // Placeholder
            void VSGetConstantBuffers(); // Placeholder
            void PSGetShaderResources(); // Placeholder
            void PSGetShader(); // Placeholder
            void PSGetSamplers(); // Placeholder
            void VSGetShader(); // Placeholder
            void PSGetConstantBuffers(); // Placeholder
            void IAGetInputLayout(); // Placeholder
            void IAGetVertexBuffers(); // Placeholder
            void IAGetIndexBuffer(); // Placeholder
            void GSGetConstantBuffers(); // Placeholder
            void GSGetShader(); // Placeholder
            void IAGetPrimitiveTopology(); // Placeholder
            void VSGetShaderResources(); // Placeholder
            void VSGetSamplers(); // Placeholder
            void GetPredication(); // Placeholder
            void GSGetShaderResources(); // Placeholder
            void GSGetSamplers(); // Placeholder
            void OMGetRenderTargets(); // Placeholder
            void OMGetRenderTargetsAndUnorderedAccessViews(); // Placeholder
            void OMGetBlendState(); // Placeholder
            void OMGetDepthStencilState(); // Placeholder
            void SOGetTargets(); // Placeholder
            void RSGetState(); // Placeholder
            void RSGetViewports(); // Placeholder
            void RSGetScissorRects(); // Placeholder
            void HSGetShaderResources(); // Placeholder
            void HSGetShader(); // Placeholder
            void HSGetSamplers(); // Placeholder
            void HSGetConstantBuffers(); // Placeholder
            void DSGetShaderResources(); // Placeholder
            void DSGetShader(); // Placeholder
            void DSGetSamplers(); // Placeholder
            void DSGetConstantBuffers(); // Placeholder
            void CSGetShaderResources(); // Placeholder
            void CSGetUnorderedAccessViews(); // Placeholder
            void CSGetShader(); // Placeholder
            void CSGetSamplers(); // Placeholder
            void CSGetConstantBuffers(); // Placeholder
            void ClearState(); // Placeholder
            void Flush(); // Placeholder
            void GetType(); // Placeholder
            void GetContextFlags(); // Placeholder
            void FinishCommandList(); // Placeholder
        }

        [ComImport]
        [Guid("dc8e63f3-d12b-4952-b47b-5e45026a862d")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ID3D11Resource
        {
            void GetDevice([Out] out ID3D11Device ppDevice);
            void GetPrivateData(); // Placeholder
            void SetPrivateData(); // Placeholder
            void SetPrivateDataInterface(); // Placeholder
            void GetType(); // Placeholder
            void SetEvictionPriority(); // Placeholder
            void GetEvictionPriority(); // Placeholder
        }

        [ComImport]
        [Guid("6f15aaf2-d208-4e89-9fae-984409d4994d")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ID3D11Texture2D : ID3D11Resource
        {
            // ID3D11Resource methods
            new void GetDevice([Out] out ID3D11Device ppDevice);
            new void GetPrivateData();
            new void SetPrivateData();
            new void SetPrivateDataInterface();
            new void GetType();
            new void SetEvictionPriority();
            new void GetEvictionPriority();

            // ID3D11Texture2D methods
            void GetDesc([Out] out D3D11_TEXTURE2D_DESC pDesc);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct D3D11_TEXTURE2D_DESC
        {
            public uint Width;
            public uint Height;
            public uint MipLevels;
            public uint ArraySize;
            public int Format; // DXGI_FORMAT
            public DXGI_SAMPLE_DESC SampleDesc;
            public D3D11_USAGE Usage;
            public uint BindFlags;
            public uint CPUAccessFlags;
            public uint MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_SAMPLE_DESC
        {
            public uint Count;
            public uint Quality;
        }

        public enum D3D11_USAGE
        {
            D3D11_USAGE_DEFAULT = 0,
            D3D11_USAGE_IMMUTABLE = 1,
            D3D11_USAGE_DYNAMIC = 2,
            D3D11_USAGE_STAGING = 3
        }

        public enum D3D11_MAP
        {
            D3D11_MAP_READ = 1,
            D3D11_MAP_WRITE = 2,
            D3D11_MAP_READ_WRITE = 3,
            D3D11_MAP_WRITE_DISCARD = 4,
            D3D11_MAP_WRITE_NO_OVERWRITE = 5
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct D3D11_MAPPED_SUBRESOURCE
        {
            public IntPtr pData;
            public uint RowPitch;
            public uint DepthPitch;
        }

        [DllImport("d3d11.dll", PreserveSig = false)]
        public static extern void D3D11CreateDevice(
            IntPtr pAdapter,
            int DriverType,
            IntPtr Software,
            uint Flags,
            [In] IntPtr pFeatureLevels,
            uint FeatureLevels,
            uint SDKVersion,
            [Out] out ID3D11Device ppDevice,
            [Out] out int pFeatureLevel,
            [Out] out ID3D11DeviceContext ppImmediateContext);

        public const int D3D_DRIVER_TYPE_HARDWARE = 1;
        public const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
        public const int D3D11_SDK_VERSION = 7;
        public const int DXGI_FORMAT_B8G8R8A8_UNORM = 87;
        public const uint D3D11_CPU_ACCESS_READ = 0x20000;
    }
}
