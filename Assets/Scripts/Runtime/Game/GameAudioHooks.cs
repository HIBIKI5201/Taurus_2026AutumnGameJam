using System.Collections.Generic;
using SymphonyFrameWork.System;
using SymphonyFrameWork.System.ServiceLocate;
using Taurus.Runtime.Pendulum;
using UnityEngine;

namespace Taurus.Runtime.Game
{
    /// <summary>
    /// GameController の各種イベントを受け取り、SE / BGM を再生するオーディオ接続フック。
    /// SymphonyFramework の AudioManager（AudioConfig 設定時）またはローカル AudioSource を使用し、
    /// 未設定のクリップは安全に無音スキップされる。
    /// </summary>
    public class GameAudioHooks : MonoBehaviour
    {
        [SerializeField] private GameController _gameController;
        [SerializeField] private AudioSource _seSource;
        [SerializeField] private AudioSource _bgmSource;

        [Header("Symphony AudioManager（AudioConfig 設定時はこちらを優先）")]
        [SerializeField] private AudioGroupTypeEnum _seAudioGroup = AudioGroupTypeEnum.None;
        [SerializeField] private AudioGroupTypeEnum _bgmAudioGroup = AudioGroupTypeEnum.None;

        [Header("SE Clips（未設定時は無音）")]
        [SerializeField] private AudioClip _titlePressClip; // タイトルでSpaceを押した音
        [SerializeField] private AudioClip _gameStartClip;  // ルール画面からゲームを開始した音
        [SerializeField] private AudioClip _countUpClip;    // ゲーム中に+1した音
        [SerializeField] private AudioClip _countDownClip;  // ゲーム中に-1した音
        [SerializeField] private AudioClip _parentTopClip;  // 親玉の真上通過チャイム
        [SerializeField] private AudioClip _childTopClip;   // 子玉の真上通過チャイム
        [SerializeField] private AudioClip _waveSuccessClip;// Wave正解ジングル
        [SerializeField] private AudioClip _gameOverClip;   // 不一致・ゲームオーバージングル
        [SerializeField] private AudioClip _waveStartClip;  // Wave開始ホイッスル
        [SerializeField] private AudioClip _countFloorClip; // カウンター0で減算しようとした時のキャンセル音
        [SerializeField] private AudioClip _recordHighClip; // トップ3入りした時の新記録ジングル

        [Header("Voice Clips（未設定時は無音）")]
        [SerializeField] private AudioClip _startVoiceClip;         // ゲーム開始（ルール説明から・リトライ）
        [SerializeField] private AudioClip _waveClearVoiceClip;     // Wave 正解（次のステージに行く前）
        [SerializeField] private AudioClip _retireVoiceClip;        // Esc で途中終了したとき
        [SerializeField] private AudioClip _resultRank1VoiceClip;   // リザルト：ランキング1位になったとき
        [SerializeField] private AudioClip _resultRank2To3VoiceClip;// リザルト：ランキング2位・3位に入ったとき
        [SerializeField] private AudioClip _resultRankOutVoiceClip; // リザルト：ランキングに入らなかったとき

        [Tooltip("ボイスの再生音量倍率")]
        [SerializeField, Range(0f, 1.5f)] private float _voiceVolumeScale = 1.0f;

        [Tooltip("ゲームオーバー音からリザルトのボイスまでの間隔（秒）")]
        [SerializeField] private float _resultVoiceDelaySeconds = 0.6f;

        [Tooltip("ゲームオーバー音から新記録ジングルまでの間隔（秒）")]
        [SerializeField] private float _recordHighDelaySeconds = 1.0f;

        [Header("BGM Clips（未設定時は無音）")]
        [SerializeField] private AudioClip _titleBgmClip;   // タイトル・ルール説明BGM
        [SerializeField] private AudioClip _gameBgmClip;    // インゲームWave中BGM
        [SerializeField] private AudioClip _resultBgmClip;  // リザルト画面BGM（クリアWave数が少ないとき：しんみり）
        [SerializeField] private AudioClip _resultHighBgmClip; // リザルト画面BGM（クリアWave数が多いとき：明るい）

        [Tooltip("クリアしたWave数がこの値以上なら、リザルトで明るいBGM（_resultHighBgmClip）を流す")]
        [SerializeField] private int _resultHighMinClearedWaves = 4;

        [Header("SE 冒頭無音トリミング（再生遅延解消・音量補正）")]
        [Tooltip("ON のとき、SE クリップ先頭の無音区間を自動検出し、鳴り始めから即座に再生する")]
        [SerializeField] private bool _autoTrimLeadingSilence = true;

        [Tooltip("無音とみなす振幅しきい値（この値を超えたフレームから再生する）")]
        [SerializeField, Range(0.001f, 0.1f)] private float _silenceThreshold = 0.015f;

        [Tooltip("SE 全体の再生音量倍率")]
        [SerializeField, Range(0f, 1.5f)] private float _seVolumeScale = 1.0f;

        private readonly Dictionary<AudioClip, AudioClip> _trimmedClipCache = new Dictionary<AudioClip, AudioClip>();
        private GameController _subscribedController;
        private bool _hasObservedPhase;

        // Esc で途中終了したときはリザルトのボイスの代わりにリタイアのボイスを流す
        private bool _isRetiring;
        private AudioClip _pendingResultVoice;
        private GamePhase _lastPhase;
        // 減算時に「0のまま」だったかを判定するため、直前のカウントを覚えておく
        private int _lastPlayerCount;

        private void OnEnable()
        {
            ResolveAndBindGameController();
        }

        private void OnDisable()
        {
            UnbindGameController();
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<AudioClip, AudioClip> pair in _trimmedClipCache)
            {
                if (pair.Value != null && !ReferenceEquals(pair.Value, pair.Key))
                {
                    Destroy(pair.Value);
                }
            }

            _trimmedClipCache.Clear();
        }

        /// <summary> エディタ構築ツール（ProjectSceneSetupTool）から参照を設定する。 </summary>
        public void ConfigureReferences(GameController gameController, AudioSource seSource, AudioSource bgmSource)
        {
            _gameController = gameController;
            _seSource = seSource;
            _bgmSource = bgmSource;
        }

        /// <summary> SE / BGM クリップを一括設定する（主にテスト・エディタ構築用）。 </summary>
        public void ConfigureClips(
            AudioClip titlePressClip,
            AudioClip gameStartClip,
            AudioClip countUpClip,
            AudioClip countDownClip,
            AudioClip parentTopClip,
            AudioClip childTopClip,
            AudioClip waveSuccessClip,
            AudioClip gameOverClip,
            AudioClip titleBgmClip,
            AudioClip gameBgmClip,
            AudioClip resultBgmClip)
        {
            _titlePressClip = titlePressClip;
            _gameStartClip = gameStartClip;
            _countUpClip = countUpClip;
            _countDownClip = countDownClip;
            _parentTopClip = parentTopClip;
            _childTopClip = childTopClip;
            _waveSuccessClip = waveSuccessClip;
            _gameOverClip = gameOverClip;
            _titleBgmClip = titleBgmClip;
            _gameBgmClip = gameBgmClip;
            _resultBgmClip = resultBgmClip;
        }

        /// <summary> 追加の SE クリップ（Wave開始・下限キャンセル・新記録）を設定する（主にテスト・エディタ構築用）。 </summary>
        public void ConfigureExtraClips(AudioClip waveStartClip, AudioClip countFloorClip, AudioClip recordHighClip)
        {
            _waveStartClip = waveStartClip;
            _countFloorClip = countFloorClip;
            _recordHighClip = recordHighClip;
        }

        /// <summary> クリアWave数が多いとき用のリザルトBGMを設定する（主にテスト・エディタ構築用）。 </summary>
        public void ConfigureResultHighBgm(AudioClip resultHighBgmClip)
        {
            _resultHighBgmClip = resultHighBgmClip;
        }

        private void ResolveAndBindGameController()
        {
            if (ServiceLocator.TryGetInstance<GameController>(out GameController located))
            {
                BindGameController(located);
                return;
            }

            if (_gameController != null)
            {
                BindGameController(_gameController);
            }

            ServiceLocator.RegisterAfterLocate<GameController>(HandleGameControllerLocated);
        }

        private void HandleGameControllerLocated(GameController controller)
        {
            if (this == null || !isActiveAndEnabled)
            {
                return;
            }

            BindGameController(controller);
        }

        private void BindGameController(GameController controller)
        {
            if (controller == null || ReferenceEquals(_subscribedController, controller))
            {
                return;
            }

            UnbindGameController();
            _subscribedController = controller;
            _gameController = controller;

            _subscribedController.OnPhaseChanged += HandlePhaseChanged;
            _subscribedController.OnTitlePressChanged += HandleTitlePressChanged;
            _subscribedController.OnWaveStarted += HandleWaveStarted;
            _subscribedController.OnPlayerCountChanged += HandlePlayerCountChanged;
            _subscribedController.OnPendulumTopCrossed += HandlePendulumTopCrossed;
            _subscribedController.OnWaveSucceeded += HandleWaveSucceeded;
            _subscribedController.OnGameFinished += HandleGameFinished;
            _subscribedController.OnRunRetired += HandleRunRetired;

            // 既に初期フェーズへ遷移済みの場合もBGMが鳴るように現在状態を同期する
            _lastPhase = _subscribedController.CurrentPhase;
            _hasObservedPhase = true;
            ApplyBgmForPhase(_subscribedController.CurrentPhase);
        }

        private void UnbindGameController()
        {
            if (_subscribedController == null)
            {
                return;
            }

            _subscribedController.OnPhaseChanged -= HandlePhaseChanged;
            _subscribedController.OnTitlePressChanged -= HandleTitlePressChanged;
            _subscribedController.OnWaveStarted -= HandleWaveStarted;
            _subscribedController.OnPlayerCountChanged -= HandlePlayerCountChanged;
            _subscribedController.OnPendulumTopCrossed -= HandlePendulumTopCrossed;
            _subscribedController.OnWaveSucceeded -= HandleWaveSucceeded;
            _subscribedController.OnGameFinished -= HandleGameFinished;
            _subscribedController.OnRunRetired -= HandleRunRetired;
            _subscribedController = null;
            _hasObservedPhase = false;
        }

        private void HandlePhaseChanged(GamePhase phase)
        {
            if (_hasObservedPhase)
            {
                if (_lastPhase == GamePhase.Rules && phase == GamePhase.Playing)
                {
                    PlaySe(_gameStartClip);
                    PlayVoice(_startVoiceClip);
                }
                else if (_lastPhase == GamePhase.Result && phase == GamePhase.Playing)
                {
                    // リザルトからのリトライもゲーム開始として声を出す
                    PlayVoice(_startVoiceClip);
                }
                else if (_lastPhase == GamePhase.Result && phase == GamePhase.Title)
                {
                    PlaySe(_titlePressClip);
                }
            }

            _lastPhase = phase;
            _hasObservedPhase = true;

            // リザルトのBGMはクリアWave数で選ぶため、数が分かる OnGameFinished で流す
            // （GameController は Result への遷移を通知した直後に OnGameFinished を通知する）
            if (phase != GamePhase.Result)
            {
                ApplyBgmForPhase(phase);
            }
        }

        private void ApplyBgmForPhase(GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.Title:
                case GamePhase.Rules:
                    PlayBgm(_titleBgmClip);
                    break;
                case GamePhase.Playing:
                case GamePhase.Success:
                    PlayBgm(_gameBgmClip);
                    break;
                case GamePhase.Result:
                    PlayBgm(_resultBgmClip);
                    break;
            }
        }

        private void HandleTitlePressChanged(int current, int required)
        {
            // リセット時の 0 通知では鳴らさない
            if (current > 0)
            {
                PlaySe(_titlePressClip);
            }
        }

        private void HandleWaveStarted(int wave)
        {
            PlaySe(_waveStartClip);
        }

        private void HandlePlayerCountChanged(int currentCount, int delta)
        {
            if (delta > 0)
            {
                PlaySe(_countUpClip);
            }
            else if (delta < 0)
            {
                // 0 のまま減算しようとした（下限で止まった）ときはキャンセル音、それ以外は通常の減算音
                bool hitFloor = IsCountFloorHit(_lastPlayerCount, currentCount);
                PlaySe(hitFloor && _countFloorClip != null ? _countFloorClip : _countDownClip);
            }

            _lastPlayerCount = currentCount;
        }

        private void HandlePendulumTopCrossed(PendulumMotion pendulum, bool isChild, int crossedCount)
        {
            PlaySe(isChild ? _childTopClip : _parentTopClip);
        }

        private void HandleWaveSucceeded(int expectedCount, int nextWave)
        {
            PlaySe(_waveSuccessClip);
            PlayVoice(_waveClearVoiceClip);
        }

        private void HandleGameFinished(int clearedWaves, int expectedCount, int playerCount, IReadOnlyList<int> records)
        {
            PlayBgm(SelectResultBgm(clearedWaves));
            PlaySe(_gameOverClip);

            // リタイアならリタイアの声、そうでなければ今回の順位で声を変える（ゲームオーバー音の少し後）
            _pendingResultVoice = _isRetiring ? _retireVoiceClip : SelectResultVoice(clearedWaves, records);
            _isRetiring = false;
            CancelInvoke(nameof(PlayPendingResultVoice));
            Invoke(nameof(PlayPendingResultVoice), Mathf.Max(0f, _resultVoiceDelaySeconds));

            // 今回の記録がトップ3に入っていれば、ゲームオーバー音の後に新記録ジングルを鳴らす
            if (IsTopRecord(clearedWaves, records))
            {
                CancelInvoke(nameof(PlayRecordHigh));
                Invoke(nameof(PlayRecordHigh), Mathf.Max(0f, _recordHighDelaySeconds));
            }
        }

        // 今回のクリア数がトップ3の何位に入ったか（1〜3）。入っていなければ 0
        public static int GetRecordRank(int clearedWaves, IReadOnlyList<int> records)
        {
            if (clearedWaves <= 0 || records == null)
            {
                return 0;
            }

            for (int i = 0; i < records.Count && i < 3; i++)
            {
                if (records[i] == clearedWaves)
                {
                    return i + 1;
                }
            }

            return 0;
        }

        private AudioClip SelectResultVoice(int clearedWaves, IReadOnlyList<int> records)
        {
            switch (GetRecordRank(clearedWaves, records))
            {
                case 1: return _resultRank1VoiceClip;
                case 2:
                case 3: return _resultRank2To3VoiceClip;
                default: return _resultRankOutVoiceClip;
            }
        }

        private void HandleRunRetired()
        {
            _isRetiring = true;
        }

        private void PlayPendingResultVoice()
        {
            PlayVoice(_pendingResultVoice);
            _pendingResultVoice = null;
        }

        // ボイスは SE と同じ出力に、ボイス用の音量で重ねて鳴らす（冒頭の無音は SE と同じく詰める）
        private void PlayVoice(AudioClip clip)
        {
            AudioSource source = ResolveAudioSource(_seAudioGroup, _seSource);
            if (source == null || clip == null)
            {
                return;
            }

            AudioClip playable = _autoTrimLeadingSilence ? GetOrCreateTrimmedClip(clip) : clip;
            source.PlayOneShot(playable, Mathf.Clamp(_voiceVolumeScale, 0f, 1.5f));
        }

        private void PlayRecordHigh()
        {
            PlaySe(_recordHighClip);
        }

        /// <summary> クリアWave数に応じたリザルトBGMを返す。多いとき用が未設定なら少ないとき用を使う。 </summary>
        private AudioClip SelectResultBgm(int clearedWaves)
        {
            if (IsHighResult(clearedWaves, _resultHighMinClearedWaves) && _resultHighBgmClip != null)
            {
                return _resultHighBgmClip;
            }

            return _resultBgmClip;
        }

        /// <summary> クリアWave数が「多い」側（明るいリザルトBGM）に入るかを返す。 </summary>
        public static bool IsHighResult(int clearedWaves, int highMinClearedWaves)
        {
            return clearedWaves >= highMinClearedWaves;
        }

        /// <summary> 減算の通知で、減算前も後も 0 だった（下限で止まった）かを返す。 </summary>
        public static bool IsCountFloorHit(int previousCount, int currentCount)
        {
            return previousCount == 0 && currentCount == 0;
        }

        /// <summary> 今回の記録がトップ3の記録一覧に入っているかを返す。0 Wave（1つもクリアしていない）は記録扱いしない。 </summary>
        public static bool IsTopRecord(int clearedWaves, IReadOnlyList<int> records)
        {
            if (clearedWaves <= 0 || records == null)
            {
                return false;
            }

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] == clearedWaves)
                {
                    return true;
                }
            }

            return false;
        }

        private void PlaySe(AudioClip clip)
        {
            AudioSource source = ResolveAudioSource(_seAudioGroup, _seSource);
            if (source == null || clip == null)
            {
                return;
            }

            AudioClip playable = _autoTrimLeadingSilence ? GetOrCreateTrimmedClip(clip) : clip;
            source.PlayOneShot(playable, Mathf.Clamp(_seVolumeScale, 0f, 1.5f));
        }

        /// <summary>
        /// PCM サンプル配列の先頭から無音区間を走査し、最初に振幅が threshold を超えるフレーム位置を返す。
        /// </summary>
        public static int FindFirstAudibleFrame(float[] samples, int channels, float threshold)
        {
            if (samples == null || samples.Length == 0 || channels <= 0)
            {
                return 0;
            }

            int totalFrames = samples.Length / channels;
            float clampedThreshold = Mathf.Max(0.0001f, threshold);
            for (int frame = 0; frame < totalFrames; frame++)
            {
                int baseIndex = frame * channels;
                for (int ch = 0; ch < channels; ch++)
                {
                    if (Mathf.Abs(samples[baseIndex + ch]) >= clampedThreshold)
                    {
                        return frame;
                    }
                }
            }

            return 0;
        }

        private AudioClip GetOrCreateTrimmedClip(AudioClip original)
        {
            if (original == null)
            {
                return null;
            }

            if (_trimmedClipCache.TryGetValue(original, out AudioClip cached) && cached != null)
            {
                return cached;
            }

            AudioClip result = original;
            try
            {
                if (original.loadType == AudioClipLoadType.DecompressOnLoad && original.samples > 0 && original.channels > 0)
                {
                    float[] samples = new float[original.samples * original.channels];
                    if (original.GetData(samples, 0))
                    {
                        int firstAudible = FindFirstAudibleFrame(samples, original.channels, _silenceThreshold);
                        // 約 2ms の余裕を残してクリックノイズを防ぐ
                        int marginFrames = Mathf.RoundToInt(original.frequency * 0.002f);
                        int trimStart = Mathf.Max(0, firstAudible - marginFrames);
                        int remainingFrames = original.samples - trimStart;

                        // 10ms 以上の無音がある場合のみトリミング版クリップを生成
                        int minTrimFrames = Mathf.RoundToInt(original.frequency * 0.010f);
                        if (trimStart >= minTrimFrames && remainingFrames > 64)
                        {
                            float[] trimmedSamples = new float[remainingFrames * original.channels];
                            System.Array.Copy(samples, trimStart * original.channels, trimmedSamples, 0, trimmedSamples.Length);

                            AudioClip trimmed = AudioClip.Create(
                                $"{original.name}_Trimmed",
                                remainingFrames,
                                original.channels,
                                original.frequency,
                                false);
                            trimmed.SetData(trimmedSamples, 0);
                            result = trimmed;
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                result = original;
            }

            _trimmedClipCache[original] = result;
            return result;
        }

        private void PlayBgm(AudioClip clip)
        {
            AudioSource source = ResolveAudioSource(_bgmAudioGroup, _bgmSource);
            if (source == null || clip == null)
            {
                return;
            }

            // 同じBGMが既に再生中なら途切れさせない（Title → Rules 遷移時など）
            if (source.clip == clip && source.isPlaying)
            {
                return;
            }

            source.clip = clip;
            source.loop = true;
            source.Play();
        }

        private static AudioSource ResolveAudioSource(AudioGroupTypeEnum group, AudioSource fallback)
        {
            if (group != AudioGroupTypeEnum.None)
            {
                AudioSource managedSource = AudioManager.GetAudioSource(group.ToString());
                if (managedSource != null)
                {
                    return managedSource;
                }
            }

            return fallback;
        }
    }
}
