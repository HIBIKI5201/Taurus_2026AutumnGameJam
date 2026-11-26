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

        [Tooltip("ゲームオーバー音から新記録ジングルまでの間隔（秒）")]
        [SerializeField] private float _recordHighDelaySeconds = 1.0f;

        [Header("BGM Clips（未設定時は無音）")]
        [SerializeField] private AudioClip _titleBgmClip;   // タイトル・ルール説明BGM
        [SerializeField] private AudioClip _gameBgmClip;    // インゲームWave中BGM
        [SerializeField] private AudioClip _resultBgmClip;  // リザルト画面BGM

        private GameController _subscribedController;
        private bool _hasObservedPhase;
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
                }
                else if (_lastPhase == GamePhase.Result && phase == GamePhase.Title)
                {
                    PlaySe(_titlePressClip);
                }
            }

            _lastPhase = phase;
            _hasObservedPhase = true;
            ApplyBgmForPhase(phase);
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
        }

        private void HandleGameFinished(int clearedWaves, int expectedCount, int playerCount, IReadOnlyList<int> records)
        {
            PlaySe(_gameOverClip);

            // 今回の記録がトップ3に入っていれば、ゲームオーバー音の後に新記録ジングルを鳴らす
            if (IsTopRecord(clearedWaves, records))
            {
                CancelInvoke(nameof(PlayRecordHigh));
                Invoke(nameof(PlayRecordHigh), Mathf.Max(0f, _recordHighDelaySeconds));
            }
        }

        private void PlayRecordHigh()
        {
            PlaySe(_recordHighClip);
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
            if (source != null && clip != null)
            {
                source.PlayOneShot(clip);
            }
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
