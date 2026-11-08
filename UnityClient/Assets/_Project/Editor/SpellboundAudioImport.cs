// Audio/Resources/Sounds 가져오기 설정 (설명: Docs/Sound.md)
//   BGM_* = Streaming (Quest 메모리 절약, 크기 보정 안 함), 그 외 = Decompress On Load (SoundManager 가 GetData 로 크기를 잰다)
//   이미 가져온 파일에 다시 적용하려면 Sounds 폴더에서 Reimport.

using UnityEditor;
using UnityEngine;

namespace SpellboundVR.EditorTools
{
    public sealed class SpellboundAudioImport : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Audio/Resources/Sounds/")) return;
            var importer = (AudioImporter)assetImporter;
            bool music = System.IO.Path.GetFileName(assetPath).StartsWith("BGM_");
            var s = importer.defaultSampleSettings;
            s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = music ? 0.6f : 0.8f;
            importer.defaultSampleSettings = s;
            importer.loadInBackground = music;
        }

        [MenuItem("Spellbound/Debug/Sound Self-Check")]
        private static void SelfCheck()
        {
            // 0.5 크기 사각파 1초 + 무음 1초 → 게이트 RMS = 0.5 (무음 구간은 빼고 잰다)
            var data = new float[88200];
            for (int i = 0; i < 44100; i++) data[i] = (i / 50) % 2 == 0 ? 0.5f : -0.5f;
            float rms = Audio.SoundManager.GatedRms(data, 2205);
            Debug.Assert(Mathf.Abs(rms - 0.5f) < 1e-3f, "[Sound] GatedRms 틀림: " + rms);
            var clips = Resources.LoadAll<AudioClip>("Sounds");
            Debug.Assert(clips.Length == 77, "[Sound] 클립 수 " + clips.Length + " (기대 77)");
            Debug.Log($"[Sound] Self-Check — GatedRms {rms:0.000} (기대 0.500), 클립 {clips.Length}개");
        }
    }
}
