using System.Collections;
using AOT;
using UnityEngine;
using System.Runtime.InteropServices;
using UnityEngine.UI;
using UnityEngine.Experimental.Rendering;
using System;
using NUnit.Framework;
using UnityEngine.Assertions;


#if USE_PICOXR && UNITY_ANDROID && !UNITY_EDITOR
using Unity.XR.PICO.TOBSupport;
using Unity.XR.PXR;
#endif

public class EnterpriseCameraAccessManager : MonoBehaviour
{
    public static EnterpriseCameraAccessManager Instance { get; private set; }

    //Move from Material to render texture to be compatiabe with gemini.cs
    public RenderTexture PreviewRenderTexture;
    [Tooltip("WebCam will be used for Editor mode or Smartphone. Default camera is used if this field is empty.")]
    public string WebCamDeviceName = "";

    private WebCamTexture webCamTexture;
    private Texture2D tmpTexture = null;
    private string tempBase64String = null;
    private float skipSeconds = 0.1f;
#if UNITY_VISIONOS && !UNITY_EDITOR
    private bool _hasSetTexture = false;
    private Texture2D _texture;
    private RenderTexture _renderTexture;
    private IntPtr _texturePtr;
    private int _width = 1920;
    private int _height = 1080;
#endif

#if USE_PICOXR && UNITY_ANDROID && !UNITY_EDITOR
    private int PicoImageWidth = 1164;
    private int PicoImageHeight = 874;
#endif


    ///<summary
    /// Get Vision pro main camera as RenderTexture 
    /// 
    ///</summary>
    /// <returns></returns>
    public RenderTexture GetMainCameraRenderTexture()
    {
        return PreviewRenderTexture;
    }

    /// <summary>
    /// Get Vision Pro main camera image as texture2D.
    /// </summary>
    /// <returns></returns>
    public Texture2D GetMainCameraTexture2D()
    {
#if UNITY_VISIONOS && !UNITY_EDITOR
        Base64ToTexture2D(tmpTexture, tempBase64String);
#endif
        return tmpTexture;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this.gameObject); }
        else { Instance = this; DontDestroyOnLoad(this.gameObject); }

        //Ensure the PreviewRenderTexture is set and complies with 1920x1080 resolution
        if (PreviewRenderTexture == null)
        {
            Debug.LogError("PreviewRenderTexture is not set. Please assign a RenderTexture in the inspector.");
        }
        else
        {
            if (PreviewRenderTexture.height == 1080 && PreviewRenderTexture.width == 1920)
            {
                Debug.Log($"Using predefined RenderTexture with resolution {PreviewRenderTexture.width}x{PreviewRenderTexture.height}");
                Debug.Log($"PreviewRenderTexture color format: {PreviewRenderTexture.graphicsFormat}");
                Debug.Log($"PreviewRenderTexture depth format: {PreviewRenderTexture.depthStencilFormat}");
            }
        }
    }

    void OnEnable()
    {
#if UNITY_VISIONOS && !UNITY_EDITOR
        // No setup required for native texture capture
#endif
    }

    void Start()
    {

#if USE_PICOXR && UNITY_ANDROID && !UNITY_EDITOR
        PicoStart();
        return;
#endif

#if UNITY_VISIONOS && !UNITY_EDITOR
        startCapture();
        return;
#endif

#if UNITY_EDITOR
        StartWebCam(WebCamDeviceName);
#elif UNITY_IOS
        StartCoroutine(RequestCameraPermission_iOS());
#elif UNITY_ANDROID
        StartCoroutine(RequestCameraPermission_Android());
#else
        StartWebCam(WebCamDeviceName);
#endif
    }

    void OnDisable()
    {
#if USE_PICOXR && UNITY_ANDROID && !UNITY_EDITOR
        OnPicoDisable();
        return;
#endif

#if UNITY_VISIONOS && !UNITY_EDITOR
        stopCapture();
        return;
#endif
        if (webCamTexture != null) { webCamTexture.Stop(); }
    }

    IEnumerator RequestCameraPermission_iOS()
    {
        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        if (Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            StartWebCam(WebCamDeviceName);
        }
        else
        {
            Debug.Log("Permission denied.");
        }
    }

    IEnumerator RequestCameraPermission_Android()
    {
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            Application.RequestUserAuthorization(UserAuthorization.WebCam);
            yield return new WaitForSeconds(1); // Wait for the result of the authorization request
        }

        if (Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            StartWebCam(WebCamDeviceName);
        }
        else
        {
            Debug.Log("Permission denied");
        }
    }

    void Update()
    {
#if UNITY_VISIONOS && !UNITY_EDITOR
        if (_hasSetTexture)
        {
            UpdateTexture();
        }
        else
        {
            TryGetTexture();
        }
#else
        // Apply WebCamTexture to material
        ApplyWebCamTextureToRenderTexture(webCamTexture);

#if USE_PICOXR && UNITY_ANDROID && !UNITY_EDITOR
        ApplyPicoFrameToMaterial(PreviewMaterial);
#endif
#endif
    }

    void StartWebCam(string deviceName)
    {
        webCamTexture = new WebCamTexture(deviceName);
        webCamTexture.Play();
    }

    // Call function continuously
    IEnumerator ApplyVisionProCameraCaptureToMaterialContinuously()
    {
        while (true)
        {
            yield return new WaitForSeconds(skipSeconds);
            ApplyBase64StringToRenderTexture(tempBase64String);
        }
    }


    void ApplyWebCamTextureToRenderTexture(WebCamTexture webCamTexture)
    {
        if (webCamTexture == null) { return; }
        if (PreviewRenderTexture == null) { return; }
        if (webCamTexture.width <= 16) { return; }
        if (webCamTexture.isPlaying == false) { return; }

        Graphics.Blit(webCamTexture, PreviewRenderTexture);
    }

    void ApplyBase64StringToMaterial(Material material, string base64String)
    {
        if (base64String == null) { return; }

        // Overwrite the tmpTexture (material.mainTexture) with the base64String
        Base64ToTexture2D(tmpTexture, base64String);
    }

    void ApplyBase64StringToRenderTexture(string base64String)
    {
        if (base64String == null) { return; }
        if (PreviewRenderTexture == null) { return; }

        //Create a temporary texture from base64 and blit to render texture

        if (tmpTexture == null)
        {
            tmpTexture = new Texture2D(PreviewRenderTexture.width, PreviewRenderTexture.height);
        }
        Base64ToTexture2D(tmpTexture, base64String);
        Graphics.Blit(tmpTexture, PreviewRenderTexture);
    }

    // Convert Base64String to Texture2D
    void Base64ToTexture2D(Texture2D tex, string base64)
    {
        try
        {
            byte[] imageBytes = System.Convert.FromBase64String(base64);

            // tmpTexture に画像を読み込む
            bool loadSuccess = tex.LoadImage(imageBytes);

            if (!loadSuccess)
            {
                Debug.LogError("Failed to load image from byte array.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to convert base64 string to texture2D: {ex.Message}");
        }
    }

    bool IsVisionOs()
    {
#if UNITY_VISIONOS && !UNITY_EDITOR
        return true;
#endif
        return false;
    }

    delegate void CallbackDelegate(string command);
    [MonoPInvokeCallback(typeof(CallbackDelegate))]
    static void CallbackFromNative(string command)
    {
        Instance.tempBase64String = command;
    }

#if UNITY_VISIONOS && !UNITY_EDITOR
    private void TryGetTexture()
    {
        IntPtr texturePtr = getTexture();
        if (texturePtr == IntPtr.Zero) return;

        _texturePtr = texturePtr;

        if (_texture != null)
        {
            UnityEngine.Object.Destroy(_texture);
        }

        _texture = Texture2D.CreateExternalTexture(_width, _height, TextureFormat.RGBA32, false, false, _texturePtr);
        _texture.UpdateExternalTexture(_texturePtr);
        
        // スケールとオフセットを使用して上下反転を行う
        // scale.y を -1 にすることで上下反転、offset.y を 1 にすることで位置を調整
        Vector2 scale = new Vector2(1, -1);
        Vector2 offset = new Vector2(0, 1);
        
        Graphics.Blit(_texture, PreviewRenderTexture, scale, offset);

        _hasSetTexture = true;
    }

    private void UpdateTexture()
    {
        // スケールとオフセットを使用して上下反転を行う
        Vector2 scale = new Vector2(1, -1);
        Vector2 offset = new Vector2(0, 1);
        
        Graphics.Blit(_texture, PreviewRenderTexture, scale, offset);
        Unity.PolySpatial.PolySpatialObjectUtils.MarkDirty(PreviewRenderTexture);
    }
#endif

#if UNITY_VISIONOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern void SetNativeCallbackOfCameraAccess(CallbackDelegate callback);
    [DllImport("__Internal")]
    static extern void StartVisionProMainCameraCapture();
    [DllImport("__Internal")]
    static extern void startCapture();
    [DllImport("__Internal")]
    static extern void stopCapture();
    [DllImport("__Internal")]
    static extern IntPtr getTexture();
#else
    static void SetNativeCallbackOfCameraAccess(CallbackDelegate callback) { }
    static void StartVisionProMainCameraCapture() { }
#endif

}
