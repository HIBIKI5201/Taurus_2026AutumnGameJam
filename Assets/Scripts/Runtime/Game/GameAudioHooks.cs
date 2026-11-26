using System.Collections.Generic;
using Taurus.Runtime.Pendulum;
using UnityEngine;

namespace Taurus.Runtime.Game
{
    /// <summary>
    /// GameController の各種イベントを受け取り、SE / BGM を再生するオーディオ接続フック。
    /// サウンド素材が納品されたら、インスペクターから各 AudioClip 欄へドラッグ＆ドロップするだけで鳴る。
    /// 未設定のクリップは安全に無音スキップされる。
    /// </summary>
    public class GameAudioHooks : MonoBehaviour
    {
        [SerializeField] private GameController _gameController;
        [SerializeField] private AudioSource _seSource;
        [SerializeField] private AudioSource _bgmSource;

        [Header("SE Clips（未設定時は無音）")]
        [SerializeField] private AudioClip _titlePressClip; // タイトルでSpaceを押した音
        [SerializeField] private AudioClip _countUpClip;    // ゲーム中に+1した音
        [SerializeField] private AudioClip _countDownClip;  // ゲーム中に-1した音
        [SerializeField] private AudioClip _parentTopClip;  // 親玉の真上通過チャイム
        [SerializeField] private AudioClip _childTopClip;   // 子玉の真上通過チャイム
        [SerializeField] private AudioClip _waveSuccessClip;// Wave正解ジングル
        [SerializeField] private AudioClip _gameOverClip;   // 不一致・ゲームオーバージングル

        [Header("BGM Clips（未設定時は無音）")]
        [SerializeField] private AudioClip _titleBgmClip;   // タイトル・ルール説明BGM
        [SerializeField] private AudioClip _gameBgmClip;    // インゲームWave中BGM
        [SerializeField] private AudioClip _resultBgmClip;  // リザルト画面BGM

        private void OnEnable()
        {
            if (_gameController == null)
            {
                return;
            }

            _gameController.OnPhaseChanged += HandlePhaseChanged;
            _gameController.OnTitlePressChanged += HandleTitlePressChanged;
            _gameController.OnPlayerCountChanged += HandlePlayerCountChanged;
            _gameController.OnPendulumTopCrossed += HandlePendulumTopCrossed;
            _gameController.OnWaveSucceeded += HandleWaveSucceeded;
            _gameController.OnGameFinished += HandleGameFinished;
        }

        private void OnDisable()
        {
            if (_gameController == null)
            {
                return;
            }

            _gameController.OnPhaseChanged -= HandlePhaseChanged;
            _gameController.OnTitlePressChanged -= HandleTitlePressChanged;
            _gameController.OnPlayerCountChanged -= HandlePlayerCountChanged;
            _gameController.OnPendulumTopCrossed -= HandlePendulumTopCrossed;
            _gameController.OnWaveSucceeded -= HandleWaveSucceeded;
            _gameController.OnGameFinished -= HandleGameFinished;
        }

        /// <summary> エディタ構築ツール（ProjectSceneSetupTool）から参照を設定する。 </summary>
        public void ConfigureReferences(GameController gameController, AudioSource seSource, AudioSource bgmSource)
        {
            _gameController = gameController;
            _seSource = seSource;
            _bgmSource = bgmSource;
        }

        private void HandlePhaseChanged(GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.Title:
                case GamePhase.Rules:
                    PlayBgm(_titleBgmClip);
                    break;
                case GamePhase.Playing:
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

        private void HandlePlayerCountChanged(int currentCount, int delta)
        {
            if (delta > 0)
            {
                PlaySe(_countUpClip);
            }
            else if (delta < 0)
            {
                PlaySe(_countDownClip);
            }
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
        }

        private void PlaySe(AudioClip clip)
        {
            if (_seSource != null && clip != null)
            {
                _seSource.PlayOneShot(clip);
            }
        }

        private void PlayBgm(AudioClip clip)
        {
            if (_bgmSource == null || clip == null)
            {
                return;
            }

            // 同じBGMが既に再生中なら途切れさせない（Title → Rules 遷移時など）
            if (_bgmSource.clip == clip && _bgmSource.isPlaying)
            {
                return;
            }

            _bgmSource.clip = clip;
            _bgmSource.loop = true;
            _bgmSource.Play();
        }
    }
}
