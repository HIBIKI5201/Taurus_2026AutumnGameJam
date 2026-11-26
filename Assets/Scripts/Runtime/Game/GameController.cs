using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Taurus.Runtime.Input;
using Taurus.Runtime.Pendulum;
using Taurus.Runtime.Stage;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Taurus.Runtime.Game
{
    /// <summary>
    /// タイトル・ルール・ゲーム・成功・リザルトの5状態遷移と、Wave進行・入力制御・成否判定を統括する中核クラス。
    /// GameScene の常駐オブジェクト（GameSystem）にアタッチする。
    ///
    /// 【引き継ぎ・設計ポイント】
    /// - 時間計測は Time.timeScale の影響を受けない unscaledDeltaTime / unscaledTime を使用する。
    /// - 入力は ActionMap から C# Generate した GameInputActions と UI ボタンの両方から同じ Handle〇〇Input メソッドへ集約している。
    /// - 正解カウントはフレーム落ち等の誤差を避けるため、実測イベント数ではなくスケジュール予定数の合計（_expectedCount）を使用する。
    /// - UI（HUDController）やサウンド（GameAudioHooks）とは C# event 経由で疎結合に連携している。
    /// </summary>
    public class GameController : MonoBehaviour
    {
        /// <summary> タイトル画面からルール画面へ進むのに必要な Space 押下回数。 </summary>
        public const int TITLE_REQUIRED_PRESSES = 1;

        /// <summary> 1 Wave あたりの総時間（秒）。0〜8秒が計画軌道、8〜10秒が減衰揺動。 </summary>
        public const float WAVE_DURATION_SECONDS = 10.0f;

        /// <summary> 正解時に次 Wave へ進むまでの表示待機時間（秒）。 </summary>
        public const float SUCCESS_DISPLAY_SECONDS = 1.1f;

        [Header("Scene & Data References")]
        // StageシーンをAdditiveロードして StageAnchor を取得するローダー
        [SerializeField] private StageLoader _stageLoader;

        // Wave 1〜8+ の振り子構成・重みテーブル（未設定時は StageDefinition のデフォルト表を使用）
        [SerializeField] private StageDefinition _stageDefinition;

        [Header("Pendulum Prefabs (本番3Dモデル差し替え時はここを変更)")]
        // 単振り子（1重）Prefab。PendulumMotion がアタッチされていること
        [SerializeField] private PendulumMotion _singlePendulumPrefab;

        // 二重振り子（2重）Prefab。PendulumMotion がアタッチされていること
        [SerializeField] private PendulumMotion _doublePendulumPrefab;

        [Header("Debug / Seed Override")]
        // true にすると毎回 _fixedSeed を使って同一パターンを再現できる（QA・再現テスト用）
        [SerializeField] private bool _useFixedSeed;
        [SerializeField] private uint _fixedSeed = 123456789u;

        private readonly RecordStore _recordStore = new RecordStore();
        private readonly List<PendulumMotion> _activePendulums = new List<PendulumMotion>();

        private GamePhase _currentPhase = GamePhase.Title;
        private int _titlePressCount;
        private int _currentWave = 1;
        private int _playerCount;
        private int _expectedCount;
        private float _waveElapsedSeconds;
        private float _waveRemainingSeconds = WAVE_DURATION_SECONDS;
        private uint _runSeed;
        private bool _isStageReady;

        // Success 待機中の UniTask をリセットや破棄時に安全にキャンセルするためのトークンソース
        private CancellationTokenSource _phaseCts;

        // ActionMap（GameInputActions.inputactions）から生成された入力ラッパー
        private GameInputActions _inputActions;

        /// <summary> 画面状態が切り替わったときに発行される。 </summary>
        public event Action<GamePhase> OnPhaseChanged;

        /// <summary> タイトル画面のSpace入力回数が変化したときに発行される（現在回数, 必要回数）。 </summary>
        public event Action<int, int> OnTitlePressChanged;

        /// <summary> Waveが開始されたときに発行される（Wave番号）。 </summary>
        public event Action<int> OnWaveStarted;

        /// <summary> Waveの残り時間が更新されたときに発行される（残り秒数）。 </summary>
        public event Action<float> OnTimerUpdated;

        /// <summary> プレイヤーカウントが変更されたときに発行される（現在値, 変化量）。 </summary>
        public event Action<int, int> OnPlayerCountChanged;

        /// <summary> Wave正解時に発行される（正解回数, 次Wave番号）。 </summary>
        public event Action<int, int> OnWaveSucceeded;

        /// <summary> リザルトへ遷移したときに発行される（クリアWave数, 正解数, プレイヤー入力数, トップ3記録）。 </summary>
        public event Action<int, int, int, IReadOnlyList<int>> OnGameFinished;

        /// <summary> いずれかの振り子が真上を通過したときに発行される（振り子, 子玉か, 通過回数）。 </summary>
        public event Action<PendulumMotion, bool, int> OnPendulumTopCrossed;

        /// <summary> 現在のゲーム状態。 </summary>
        public GamePhase CurrentPhase => _currentPhase;

        /// <summary> 現在のWave番号（1始まり）。 </summary>
        public int CurrentWave => _currentWave;

        /// <summary> 現在のプレイヤーカウント。 </summary>
        public int PlayerCount => _playerCount;

        /// <summary> 現在Waveの正解カウント（全玉のスケジュール予定数合計）。 </summary>
        public int ExpectedCount => _expectedCount;

        /// <summary> 現在Waveの経過時間（秒）。 </summary>
        public float WaveElapsedSeconds => _waveElapsedSeconds;

        /// <summary> 現在Waveの残り時間（秒）。 </summary>
        public float WaveRemainingSeconds => _waveRemainingSeconds;

        /// <summary> 現在のランの32bit乱数シード（デバッグ表示・再現用）。 </summary>
        public uint RunSeed => _runSeed;

        /// <summary> 現在のトップ3記録。 </summary>
        public IReadOnlyList<int> Records => _recordStore.Records;

        /// <summary> 現在稼働中の振り子一覧（DebugOverlayが参照する）。 </summary>
        public IReadOnlyList<PendulumMotion> ActivePendulums => _activePendulums;

        /// <summary> エディタ構築ツール（ProjectSceneSetupTool）から参照を設定する。 </summary>
        public void ConfigureReferences(
            StageLoader stageLoader,
            StageDefinition stageDefinition,
            PendulumMotion singlePrefab,
            PendulumMotion doublePrefab)
        {
            _stageLoader = stageLoader;
            _stageDefinition = stageDefinition;
            _singlePendulumPrefab = singlePrefab;
            _doublePendulumPrefab = doublePrefab;
        }

        private void OnEnable()
        {
            _inputActions ??= new GameInputActions();
            _inputActions.Game.CountUp.performed += HandleCountUpPerformed;
            _inputActions.Game.CountDown.performed += HandleCountDownPerformed;
            _inputActions.Game.Cancel.performed += HandleCancelPerformed;
            _inputActions.Game.Enable();
        }

        private void OnDisable()
        {
            if (_inputActions == null)
            {
                return;
            }

            _inputActions.Game.CountUp.performed -= HandleCountUpPerformed;
            _inputActions.Game.CountDown.performed -= HandleCountDownPerformed;
            _inputActions.Game.Cancel.performed -= HandleCancelPerformed;
            _inputActions.Game.Disable();
        }

        private void Start()
        {
            InitializeAsync().Forget();
        }

        private void OnDestroy()
        {
            CancelPhaseTask();
            ClearPendulums();
            _inputActions?.Dispose();
            _inputActions = null;
        }

        /// <summary> 起動時の初期処理：トップ3記録を読み込み、タイトル初期化後にデフォルトStageをAdditiveロードする。 </summary>
        private async UniTaskVoid InitializeAsync()
        {
            _recordStore.Load();
            ResetToTitle();

            if (_stageLoader != null)
            {
                StageAnchor anchor = await _stageLoader.LoadDefaultStageAsync(this.GetCancellationTokenOnDestroy());
                _isStageReady = anchor != null;
            }
            else
            {
                Debug.LogWarning($"[{nameof(GameController)}] StageLoader が未設定のためシーン内の StageAnchor を検索します。", this);
                _isStageReady = FindFirstObjectByType<StageAnchor>() != null;
            }
        }

        private void Update()
        {
            if (_currentPhase != GamePhase.Playing)
            {
                return;
            }

            // unscaledDeltaTime で 10.0 秒を計測し、全振り子へ同じ経過時刻を配る
            float dt = Time.unscaledDeltaTime;
            _waveElapsedSeconds += dt;
            _waveRemainingSeconds = Mathf.Max(0f, WAVE_DURATION_SECONDS - _waveElapsedSeconds);
            OnTimerUpdated?.Invoke(_waveRemainingSeconds);

            float now = Time.unscaledTime;
            for (int i = 0; i < _activePendulums.Count; i++)
            {
                _activePendulums[i].EvaluateAt(_waveElapsedSeconds, dt, now);
            }

            // 10秒到達時に必ず停止して完全一致判定を行う
            if (_waveElapsedSeconds >= WAVE_DURATION_SECONDS)
            {
                EvaluateWaveResult();
            }
        }

        private void HandleCountUpPerformed(InputAction.CallbackContext context)
        {
            HandleSpaceInput();
        }

        private void HandleCountDownPerformed(InputAction.CallbackContext context)
        {
            HandleCtrlInput();
        }

        private void HandleCancelPerformed(InputAction.CallbackContext context)
        {
            HandleEscapeInput();
        }

        /// <summary> Spaceキーおよび相当UIボタンの共通処理。状態に応じてゲージ加算／Wave開始／カウント+1を行う。 </summary>
        public void HandleSpaceInput()
        {
            switch (_currentPhase)
            {
                case GamePhase.Title:
                    IncrementTitlePress();
                    break;
                case GamePhase.Rules:
                    StartRunFromRules();
                    break;
                case GamePhase.Playing:
                    AddPlayerCount(1);
                    break;
            }
        }

        /// <summary> Ctrlキーおよび-1ボタンの共通処理。プレイ中のみカウントを-1する（下限0）。 </summary>
        public void HandleCtrlInput()
        {
            if (_currentPhase == GamePhase.Playing)
            {
                AddPlayerCount(-1);
            }
        }

        /// <summary> Escapeキー押下時の共通処理。プレイ中に押すと即座にリザルト画面へ移行する。 </summary>
        public void HandleEscapeInput()
        {
            if (_currentPhase == GamePhase.Playing)
            {
                FinishRun();
            }
        }

        /// <summary> ルール説明画面の開始ボタンからWave 1を開始する。 </summary>
        public void HandleStartGameInput()
        {
            if (_currentPhase == GamePhase.Rules)
            {
                StartRunFromRules();
            }
        }

        /// <summary> リザルト画面の「もう一度遊ぶ」「タイトルへ戻る」ボタンの共通処理（仕様上いずれもタイトルへ戻す）。 </summary>
        public void HandleReturnToTitleInput()
        {
            ResetToTitle();
        }

        private void IncrementTitlePress()
        {
            if (_currentPhase != GamePhase.Title)
            {
                return;
            }

            _titlePressCount = Mathf.Min(TITLE_REQUIRED_PRESSES, _titlePressCount + 1);
            OnTitlePressChanged?.Invoke(_titlePressCount, TITLE_REQUIRED_PRESSES);

            // 規定回数の入力でルール説明画面へ進む（ここではまだWave 1を始めない）
            if (_titlePressCount >= TITLE_REQUIRED_PRESSES)
            {
                SetPhase(GamePhase.Rules);
            }
        }

        private void StartRunFromRules()
        {
            if (!_isStageReady && _stageLoader != null && _stageLoader.CurrentAnchor == null)
            {
                Debug.LogWarning($"[{nameof(GameController)}] StageAnchor が未準備です。デフォルト位置で開始します。", this);
            }

            _currentWave = 1;
            _runSeed = GenerateRunSeed();
            BeginWave();
        }

        private void BeginWave()
        {
            CancelPhaseTask();
            _playerCount = 0;
            _waveElapsedSeconds = 0f;
            _waveRemainingSeconds = WAVE_DURATION_SECONDS;

            BuildWavePendulums();
            SetPhase(GamePhase.Playing);

            OnWaveStarted?.Invoke(_currentWave);
            OnPlayerCountChanged?.Invoke(_playerCount, 0);
            OnTimerUpdated?.Invoke(_waveRemainingSeconds);
        }

        /// <summary> 現在Waveの構成に従い振り子Prefabを支点へ生成し、各玉の独立スケジュールと正解合計を確定する。 </summary>
        private void BuildWavePendulums()
        {
            ClearPendulums();

            WaveConfig config = _stageDefinition != null
                ? _stageDefinition.GetWaveConfig(_currentWave)
                : StageDefinition.GetDefaultWaveConfig(_currentWave);

            StageAnchor anchor = _stageLoader != null ? _stageLoader.CurrentAnchor : FindFirstObjectByType<StageAnchor>();
            Transform parentRoot = anchor != null ? anchor.PendulumRoot : transform;

            int singleCount = config.SinglePendulumCount;
            int doubleCount = config.DoublePendulumCount;
            int totalCount = singleCount + doubleCount;
            float now = Time.unscaledTime;

            int expectedTotal = 0;

            for (int i = 0; i < totalCount; i++)
            {
                bool isDouble = i >= singleCount;
                PendulumMotion prefab = isDouble ? _doublePendulumPrefab : _singlePendulumPrefab;
                if (prefab == null)
                {
                    Debug.LogError($"[{nameof(GameController)}] {(isDouble ? "Double" : "Single")} 振り子Prefabが未設定です。", this);
                    continue;
                }

                Transform pivot = anchor != null ? anchor.GetPivot(i, totalCount) : parentRoot;
                PendulumMotion instance = Instantiate(prefab, pivot.position, Quaternion.identity, parentRoot);
                instance.name = isDouble ? $"PendulumDouble_{i + 1:00}" : $"PendulumSingle_{i + 1:00}";

                // HTML参照実装と同じシード導出式：Wave番号と玉インデックスから素数オフセットで独立シードを作る
                int ix = _currentWave * 17 + i * 2 + 1;
                uint parentSeed = unchecked(_runSeed + (uint)(ix * 7919));
                MotionSchedule parentSchedule = MotionScheduleGenerator.Generate(parentSeed, config.Choices);

                MotionSchedule childSchedule = null;
                if (isDouble)
                {
                    uint childSeed = unchecked(_runSeed + (uint)((ix + 1) * 7919));
                    childSchedule = MotionScheduleGenerator.Generate(childSeed, config.Choices);
                }

                instance.OnTopCrossed += HandlePendulumTopCrossed;
                instance.Initialize(i, parentSchedule, childSchedule, now);

                expectedTotal += instance.TotalExpectedTopCount;
                _activePendulums.Add(instance);
            }

            _expectedCount = expectedTotal;
        }

        private void AddPlayerCount(int delta)
        {
            // 判定後（Success / Result）など Playing 以外の状態ではカウント変更を受け付けない
            if (_currentPhase != GamePhase.Playing)
            {
                return;
            }

            // 下限は 0（0でCtrlを押しても負数にならない）
            int next = Mathf.Max(0, _playerCount + delta);
            _playerCount = next;
            OnPlayerCountChanged?.Invoke(_playerCount, delta);
        }

        /// <summary> 10秒終了時の完全一致判定。許容差・部分点なし。 </summary>
        private void EvaluateWaveResult()
        {
            if (_currentPhase != GamePhase.Playing)
            {
                return;
            }

            _waveRemainingSeconds = 0f;
            OnTimerUpdated?.Invoke(0f);

            if (_playerCount == _expectedCount)
            {
                // 成功したWave番号を確定し、次Wave番号へ進めて約1.1秒の成功表示へ移る
                int clearedWave = _currentWave;
                _currentWave = clearedWave + 1;
                SetPhase(GamePhase.Success);
                OnWaveSucceeded?.Invoke(_expectedCount, _currentWave);
                ProceedAfterSuccessAsync().Forget();
            }
            else
            {
                FinishRun();
            }
        }

        private async UniTaskVoid ProceedAfterSuccessAsync()
        {
            CancelPhaseTask();
            _phaseCts = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            CancellationToken token = _phaseCts.Token;

            try
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(SUCCESS_DISPLAY_SECONDS),
                    DelayType.UnscaledDeltaTime,
                    cancellationToken: token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_currentPhase == GamePhase.Success)
            {
                BeginWave();
            }
        }

        /// <summary> 失敗・途中終了時にリザルト画面へ遷移し、最後に成功したWave番号をトップ3記録へ反映する。 </summary>
        private void FinishRun()
        {
            CancelPhaseTask();
            SetPhase(GamePhase.Result);

            int clearedWaves = Mathf.Max(0, _currentWave - 1);
            IReadOnlyList<int> updatedRecords = _recordStore.RecordScore(clearedWaves);
            OnGameFinished?.Invoke(clearedWaves, _expectedCount, _playerCount, updatedRecords);
        }

        private void ResetToTitle()
        {
            CancelPhaseTask();
            ClearPendulums();

            _titlePressCount = 0;
            _currentWave = 1;
            _playerCount = 0;
            _expectedCount = 0;
            _waveElapsedSeconds = 0f;
            _waveRemainingSeconds = WAVE_DURATION_SECONDS;
            _runSeed = GenerateRunSeed();

            SetPhase(GamePhase.Title);
            OnTitlePressChanged?.Invoke(_titlePressCount, TITLE_REQUIRED_PRESSES);
            OnPlayerCountChanged?.Invoke(_playerCount, 0);
            OnTimerUpdated?.Invoke(_waveRemainingSeconds);
        }

        private void ClearPendulums()
        {
            for (int i = 0; i < _activePendulums.Count; i++)
            {
                if (_activePendulums[i] != null)
                {
                    _activePendulums[i].OnTopCrossed -= HandlePendulumTopCrossed;
                    Destroy(_activePendulums[i].gameObject);
                }
            }

            _activePendulums.Clear();
        }

        private void SetPhase(GamePhase nextPhase)
        {
            _currentPhase = nextPhase;
            OnPhaseChanged?.Invoke(_currentPhase);
        }

        private uint GenerateRunSeed()
        {
            if (_useFixedSeed)
            {
                return _fixedSeed;
            }

            // 16bitずつ結合して全32bit範囲のランダムシードを作る
            uint high = (uint)UnityEngine.Random.Range(0, 0x10000);
            uint low = (uint)UnityEngine.Random.Range(0, 0x10000);
            return (high << 16) | low;
        }

        private void CancelPhaseTask()
        {
            if (_phaseCts != null)
            {
                _phaseCts.Cancel();
                _phaseCts.Dispose();
                _phaseCts = null;
            }
        }

        private void HandlePendulumTopCrossed(PendulumMotion pendulum, bool isChild, int crossedCount)
        {
            OnPendulumTopCrossed?.Invoke(pendulum, isChild, crossedCount);
        }
    }
}
