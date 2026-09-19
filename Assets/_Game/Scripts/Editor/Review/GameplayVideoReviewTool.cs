using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace Emberfall.Editor.Review
{
    public static class GameplayVideoReviewTool
    {
        private const int CaptureCount = 37;
        private const int OutputWidth = 1280;
        private const int OutputHeight = 720;

        private static GameObject _reviewObject;
        private static VideoPlayer _player;
        private static RenderTexture _target;
        private static Texture2D _image;
        private static string _videoPath;
        private static string _outputFolder;
        private static double _duration;
        private static double _targetSeconds;
        private static int _captureIndex;
        private static DateTime _deadline;
        private static ReviewStage _stage;
        private static bool _frameReady;

        public static void CaptureLatest()
        {
            string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve project root.");
            string videoFolder = Path.Combine(projectRoot, "vedios");
            Cleanup();
            _videoPath = GetCommandLineValue("-videoPath") ?? Directory
                .EnumerateFiles(videoFolder, "*.mp4", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(_videoPath) || !File.Exists(_videoPath))
            {
                throw new FileNotFoundException("No gameplay video was found.", _videoPath);
            }

            _outputFolder = Path.Combine(
                projectRoot,
                "Builds",
                "VideoReview",
                Path.GetFileNameWithoutExtension(_videoPath));
            Directory.CreateDirectory(_outputFolder);
            foreach (string staleFrame in Directory.EnumerateFiles(_outputFolder, "frame-*.png"))
            {
                File.Delete(staleFrame);
            }

            _reviewObject = new GameObject("[Editor] Gameplay Video Review");
            _player = _reviewObject.AddComponent<VideoPlayer>();
            _target = new RenderTexture(OutputWidth, OutputHeight, 0, RenderTextureFormat.ARGB32);
            _image = new Texture2D(OutputWidth, OutputHeight, TextureFormat.RGB24, false);
            _player.playOnAwake = false;
            _player.waitForFirstFrame = true;
            _player.skipOnDrop = false;
            _player.sendFrameReadyEvents = true;
            _player.audioOutputMode = VideoAudioOutputMode.None;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.targetTexture = _target;
            _player.url = _videoPath;
            _player.frameReady += OnFrameReady;
            _captureIndex = 0;
            _stage = ReviewStage.Preparing;
            _deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
            EditorApplication.update += OnEditorUpdate;
            _player.Prepare();
            Debug.Log($"EMBERFALL_VIDEO_REVIEW_SCHEDULED source={_videoPath}");
        }

        private static void CaptureFrame(RenderTexture target, Texture2D image, string outputPath)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                image.Apply(false, false);
                File.WriteAllBytes(outputPath, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        private static void OnEditorUpdate()
        {
            try
            {
                if (_stage == ReviewStage.Preparing)
                {
                    if (!_player.isPrepared)
                    {
                        ThrowIfTimedOut("Video prepare timed out.");
                        return;
                    }

                    _duration = _player.length;
                    if (_duration <= 0.01) throw new InvalidDataException("Video decoder reported an invalid duration.");
                    Debug.Log(
                        $"EMBERFALL_VIDEO_REVIEW source={_videoPath} duration={_duration:F3}s " +
                        $"sourceSize={_player.width}x{_player.height} frames={_player.frameCount} rate={_player.frameRate:F3}");
                    BeginNextSeek();
                    return;
                }

                if (_stage == ReviewStage.Seeking)
                {
                    if (!_frameReady)
                    {
                        ThrowIfTimedOut($"Video seek timed out at {_targetSeconds:F2}s.");
                        return;
                    }

                    _player.Pause();
                    CaptureFrame(
                        _target,
                        _image,
                        Path.Combine(_outputFolder, $"frame-{_captureIndex:00}-{_targetSeconds:000.0}s.png"));
                    _captureIndex++;
                    BeginNextSeek();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Cleanup();
                EditorApplication.Exit(1);
            }
        }

        private static void BeginNextSeek()
        {
            if (_captureIndex >= CaptureCount)
            {
                File.WriteAllText(
                    Path.Combine(_outputFolder, "metadata.txt"),
                    $"source={_videoPath}{Environment.NewLine}" +
                    $"duration={_duration.ToString("F3", CultureInfo.InvariantCulture)}{Environment.NewLine}" +
                    $"size={_player.width}x{_player.height}{Environment.NewLine}" +
                    $"frameRate={_player.frameRate.ToString("F3", CultureInfo.InvariantCulture)}{Environment.NewLine}" +
                    $"frameCount={_player.frameCount}{Environment.NewLine}");
                Debug.Log($"EMBERFALL_VIDEO_REVIEW_COMPLETE output={_outputFolder}");
                Cleanup();
                EditorApplication.Exit(0);
                return;
            }

            _targetSeconds = _duration * _captureIndex / (CaptureCount - 1d);
            _frameReady = false;
            _player.time = Math.Min(_targetSeconds, Math.Max(0d, _duration - 0.05d));
            _deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            _stage = ReviewStage.Seeking;
            _player.Play();
        }

        private static void OnFrameReady(VideoPlayer source, long frameIndex)
        {
            if (_stage == ReviewStage.Seeking)
            {
                _frameReady = true;
            }
        }

        private static void ThrowIfTimedOut(string message)
        {
            if (DateTime.UtcNow >= _deadline) throw new TimeoutException(message);
        }

        private static void Cleanup()
        {
            EditorApplication.update -= OnEditorUpdate;
            if (_player != null)
            {
                _player.frameReady -= OnFrameReady;
                _player.Stop();
                _player.targetTexture = null;
            }

            if (_image != null) UnityEngine.Object.DestroyImmediate(_image);
            if (_target != null) UnityEngine.Object.DestroyImmediate(_target);
            if (_reviewObject != null) UnityEngine.Object.DestroyImmediate(_reviewObject);
            _player = null;
            _image = null;
            _target = null;
            _reviewObject = null;
            _stage = ReviewStage.Idle;
        }

        private static string GetCommandLineValue(string argumentName)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(arguments[i], argumentName, StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[i + 1];
                }
            }

            return null;
        }

        private enum ReviewStage
        {
            Idle,
            Preparing,
            Seeking
        }
    }
}
