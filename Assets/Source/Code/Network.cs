﻿using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace mofrison.Network
{
    public static class Network
    {
        public static async Task<byte[]> GetData(string url, CancellationTokenSource cancelationToken, System.Action<float> progress = null)
        {
            UnityWebRequest uwr = await SendWebRequest(UnityWebRequest.Get(url), cancelationToken, progress);
            if (uwr != null && !uwr.isHttpError && !uwr.isNetworkError)
            {
                return uwr.downloadHandler.data;
            }
            else
            {
                throw new Exception("[Netowrk] error: " + uwr.error + " " + uwr.uri);
            }
        }

        public static async Task<Texture2D> GetTexture(string url, CancellationTokenSource cancelationToken, System.Action<float> progress = null, bool caching = true)
        {
            UnityWebRequest request;

            string path = url.GetCachedPath();
            if (string.IsNullOrEmpty(path)) { request = UnityWebRequestTexture.GetTexture(url); }
            else { request = UnityWebRequestTexture.GetTexture("file://" + path); progress = null; }

            UnityWebRequest uwr = await SendWebRequest(request, cancelationToken, progress);
            if (uwr != null && !uwr.isHttpError && !uwr.isNetworkError)
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(uwr);
                texture.name = Path.GetFileName(uwr.url);
                if (caching && ResourceCache.CheckFreeSpace(await GetSize(url))) 
                {
                    ResourceCache.Caching(uwr.url, uwr.downloadHandler.data);
                }
                return texture;
            }
            else
            {
                throw new Exception("[Netowrk] error: " + uwr.error + " " + uwr.uri);
            }
        }

        public static async Task<AudioClip> GetAudioClip(string url, CancellationTokenSource cancelationToken, System.Action<float> progress = null, bool caching = true, AudioType audioType = AudioType.OGGVORBIS)
        {
            UnityWebRequest request;
            string path = url.GetCachedPath();
            if (string.IsNullOrEmpty(path)) { request = UnityWebRequestMultimedia.GetAudioClip(url, audioType); }
            else { request = UnityWebRequestMultimedia.GetAudioClip("file://" + path, audioType); progress = null; }

            UnityWebRequest uwr = await SendWebRequest(request, cancelationToken, progress);
            if (uwr != null && !uwr.isHttpError && !uwr.isNetworkError)
            {
                AudioClip audioClip = DownloadHandlerAudioClip.GetContent(uwr);
                audioClip.name = Path.GetFileName(uwr.url);

                if (caching && ResourceCache.CheckFreeSpace(await GetSize(url))) 
                { 
                    ResourceCache.Caching(uwr.url, uwr.downloadHandler.data); 
                }
                return audioClip;
            }
            else
            {
                throw new Exception("[Netowrk] error: " + uwr.error + " " + uwr.uri);
            }
        }

        private delegate void AsyncOperation();

        public static string GetVideoStream(string url, CancellationTokenSource cancelationToken, System.Action<float> progress = null, bool caching = true)
        {
            string path = url.GetCachedPath();
            if (string.IsNullOrEmpty(path))
            {
                AsyncOperation cachingVideo = async delegate {
                    if (caching && ResourceCache.CheckFreeSpace(await GetSize(url)))
                    {
                    
                        ResourceCache.Caching(url, await GetData(url, cancelationToken, progress));
                    }
                };
                cachingVideo();
                return url;
            }
            else { return path; }
        }

        public static async Task<AssetBundle> GetBundle(string url, CancellationTokenSource cancelationToken, System.Action<float> progress = null, bool caching = true)
        {
            UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(url); ;
            CachedAssetBundle cachedAssetBundle = await GetCachedAssetBundle(new System.Uri(url));
            if (Caching.IsVersionCached(cachedAssetBundle) || (caching && ResourceCache.CheckFreeSpace(await GetSize(url))))
            {
                request = UnityWebRequestAssetBundle.GetAssetBundle(url, cachedAssetBundle, 0);
            }

            UnityWebRequest uwr = await SendWebRequest(request, cancelationToken, Caching.IsVersionCached(cachedAssetBundle)? null : progress);
            if (uwr != null && !uwr.isHttpError && !uwr.isNetworkError)
            {
                AssetBundle assetBundle = DownloadHandlerAssetBundle.GetContent(uwr);
                if (caching) 
                {
                    // Deleting old versions from the cache
                    Caching.ClearOtherCachedVersions(assetBundle.name, cachedAssetBundle.hash);
                }
                return assetBundle;
            }
            else
            {
                throw new Exception("[Netowrk] error: " + uwr.error + " " + uwr.uri);
            }
        }

        private static async Task<UnityWebRequest> SendWebRequest(UnityWebRequest request, CancellationTokenSource cancelationToken = null, System.Action<float> progress = null)
        {
            while (!Caching.ready)
            {
                if (cancelationToken != null && cancelationToken.IsCancellationRequested)
                {
                    return null;
                }
                await Task.Yield();
            }

#pragma warning disable CS4014
            request.SendWebRequest();
#pragma warning restore CS4014

            while (!request.isDone)
            {
                if (cancelationToken != null && cancelationToken.IsCancellationRequested)
                {
                    request.Abort();
                    request.Dispose();

                    return null;
                }
                else
                {
                    progress?.Invoke(request.downloadProgress);
                    await Task.Yield();
                }
            }

            progress?.Invoke(1f);
            return request;
        }

        private static async Task<int> GetSize(string url)
        {
            UnityWebRequest request = await SendWebRequest(UnityWebRequest.Head(url));
            var contentLength = request.GetResponseHeader("Content-Length");
            if (int.TryParse(contentLength, out int returnValue))
            {
                return returnValue;
            }
            else
            {
                throw new Exception("[Netowrk] error: " + request.error + " " + url);
            }
        }

        private static async Task<string> GetText(string url)
        {
            var uwr = await SendWebRequest(UnityWebRequest.Get(url));
            if (uwr != null && !uwr.isHttpError && !uwr.isNetworkError)
            {
                return uwr.downloadHandler.text;
            }
            else
            {
                Debug.LogWarning("[Netowrk] error: " + uwr.error + " " + uwr.url);
                return null;
            }
        }

        private static Hash128 GetHashFromManifest(string manifest)
        {
            var hashRow = manifest.Split("\n".ToCharArray())[5];
            var hash = Hash128.Parse(hashRow.Split(':')[1].Trim());

            return hash;
        }

        private static async Task<CachedAssetBundle> GetCachedAssetBundle(System.Uri uri)
        {
            Hash128 hash = default;
            string manifest = await GetText(uri + ".manifest");

            if (!string.IsNullOrEmpty(manifest))
            {
                hash = GetHashFromManifest(manifest);
                return new CachedAssetBundle(uri.LocalPath, hash);
            }
            else
            {
                DirectoryInfo dir = new DirectoryInfo(uri.ToString().ConvertToLocalPath());
                if (dir.Exists)
                {
                    System.DateTime lastWriteTime = default;
                    foreach (var item in dir.GetDirectories())
                    {
                        if (lastWriteTime < item.LastWriteTime)
                        {
                            if (hash.isValid && hash != default) Directory.Delete(Path.Combine(dir.FullName, hash.ToString()), true);
                            lastWriteTime = item.LastWriteTime;
                            hash = Hash128.Parse(item.Name);
                        }
                        else { Directory.Delete(Path.Combine(dir.FullName, item.Name), true); }
                    }
                    return new CachedAssetBundle(uri.LocalPath, hash);
                }
                else
                {
                    throw new Exception("[Netowrk] error: Nothing was found in the cache for " + uri);
                }
            }
        }

        public class Exception : System.Exception
        {
            public Exception(string message) : base(message)
            { }
        }
    }
}