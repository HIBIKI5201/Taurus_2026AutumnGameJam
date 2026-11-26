using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using SymphonyFrameWork.System.ServiceLocate;
using Taurus.Runtime.Game;
using UnityEngine;
using UnityEngine.UI;

namespace Taurus.Runtime.UI
{
    /// <summary>
    /// Canvas 配下の Title / Rules / Game / Result 各パネルの表示切替と、
    /// Wave番号・残り時間・2桁カウンター・トップ3記録の描画を担当する UI コントローラ。
    /// GameController は ServiceLocator（未登録時は Inspector 参照）経由で安全に接続する。
    ///
    /// 【引き継ぎポイント】
    /// - 全てのボタンクリックは GameController の共通入力メソッド（HandleSpaceInput 等）へ転送しており、
    ///   キーボード操作と UI ボタン操作で挙動が食い違わないようにしている。
    /// - 本番 UI 画像を差し替える際は、Canvas 内の各 Image / Button の Sprite を差し替えるだけでそのまま動作する。
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        // カウント操作フィードバック（「カウントを記録した」等）の自動非表示までの秒数
        private const float FEEDBACK_DISPLAY_DURATION = 2f;

        [SerializeField] private GameController _gameController;

        [Header("Panels（画面状態ごとに SetActive を切替）")]
        [SerializeField] private GameObject _titlePanel;
        [SerializeField] private GameObject _rulesPanel;
        [SerializeField] private GameObject _gamePanel;
        [SerializeField] private GameObject _resultPanel;
        [SerializeField] private GameObject _successBanner;

        [Header("Title UI")]
        [SerializeField] private Text _startCountText;
        [SerializeField] private Image _startMeterFill; // Image.Type = Filled（Horizontal）を想定
        [SerializeField] private Button _titleStartButton;

        [Header("Rules UI")]
        [SerializeField] private Button _rulesStartButton;

        [Header("Game UI")]
        [SerializeField] private Text _waveText;
        [SerializeField] private Text _timerText;
        [SerializeField] private Text _counterText;
        [SerializeField] private Text _feedbackText;
        [SerializeField] private Button _plusButton;
        [SerializeField] private Button _minusButton;

        [Header("Result UI")]
        [SerializeField] private Text _clearedWavesText;
        [SerializeField] private Text _resultDetailText;
        [SerializeField] private Text[] _recordValueTexts; // 要素0=1ST, 1=2ND, 2=3RD
        [SerializeField] private Button _retryButton;
        [SerializeField] private Button _homeButton;

        [Header("Wave Transition Fade（UIの背面に描画しUIを隠さない暗転）")]
        // Wave切替時の暗転合計時間（秒）。前半で暗転、ピークでWave切替、後半で明転する
        [SerializeField, Min(0f)] private float _waveFadeDurationSeconds = 0.5f;
        // 暗転・明転に使うLitMotionのイージング（初期値は線形補間）
        [SerializeField] private Ease _waveFadeEase = Ease.Linear;
        // 暗転用オーバーレイ画像（未設定時はCanvas先頭に自動生成する）
        [SerializeField] private Image _waveFadeOverlay;
        [SerializeField] private Color _waveFadeColor = Color.black;

        private GameController _subscribedController;
        private float _feedbackHideTimer;
        private MotionHandle _waveFadeMotionHandle;

        /// <summary> Wave切替時の暗転時間（秒）。Inspectorから変更可能。 </summary>
        public float WaveFadeDurationSeconds => Mathf.Max(0f, _waveFadeDurationSeconds);

        private void Awake()
        {
            BindButtons();
            EnsureWaveFadeOverlay();
            SetWaveFadeAlpha(0f);
        }

        private void Update()
        {
            if (_feedbackHideTimer <= 0f)
            {
                return;
            }

            _feedbackHideTimer -= Time.unscaledDeltaTime;
            if (_feedbackHideTimer <= 0f && _feedbackText != null)
            {
                _feedbackText.text = string.Empty;
            }
        }

        private void OnEnable()
        {
            ServiceLocator.RegisterInstance(this, LocateTypeEnum.Locator);
            ResolveAndBindGameController();
        }

        private void OnDisable()
        {
            CancelWaveFadeMotion();
            UnbindGameController();
            ServiceLocator.UnregisterInstance(this);
        }

        /// <summary> エディタ構築ツール（ProjectSceneSetupTool）からUI参照を一括設定する。 </summary>
        public void ConfigureReferences(
            GameController gameController,
            GameObject titlePanel,
            GameObject rulesPanel,
            GameObject gamePanel,
            GameObject resultPanel,
            GameObject successBanner,
            Text startCountText,
            Image startMeterFill,
            Button titleStartButton,
            Button rulesStartButton,
            Text waveText,
            Text timerText,
            Text counterText,
            Text feedbackText,
            Button plusButton,
            Button minusButton,
            Text clearedWavesText,
            Text resultDetailText,
            Text[] recordValueTexts,
            Button retryButton,
            Button homeButton)
        {
            _gameController = gameController;
            _titlePanel = titlePanel;
            _rulesPanel = rulesPanel;
            _gamePanel = gamePanel;
            _resultPanel = resultPanel;
            _successBanner = successBanner;
            _startCountText = startCountText;
            _startMeterFill = startMeterFill;
            _titleStartButton = titleStartButton;
            _rulesStartButton = rulesStartButton;
            _waveText = waveText;
            _timerText = timerText;
            _counterText = counterText;
            _feedbackText = feedbackText;
            _plusButton = plusButton;
            _minusButton = minusButton;
            _clearedWavesText = clearedWavesText;
            _resultDetailText = resultDetailText;
            _recordValueTexts = recordValueTexts;
            _retryButton = retryButton;
            _homeButton = homeButton;
        }

        private void BindButtons()
        {
            if (_titleStartButton != null)
            {
                _titleStartButton.onClick.AddListener(HandleTitleStartButtonClicked);
            }

            if (_rulesStartButton != null)
            {
                _rulesStartButton.onClick.AddListener(HandleRulesStartButtonClicked);
            }

            if (_plusButton != null)
            {
                _plusButton.onClick.AddListener(HandlePlusButtonClicked);
            }

            if (_minusButton != null)
            {
                _minusButton.onClick.AddListener(HandleMinusButtonClicked);
            }

            if (_retryButton != null)
            {
                _retryButton.onClick.AddListener(HandleReturnToTitleClicked);
            }

            if (_homeButton != null)
            {
                _homeButton.onClick.AddListener(HandleReturnToTitleClicked);
            }
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
            _subscribedController.OnTimerUpdated += HandleTimerUpdated;
            _subscribedController.OnPlayerCountChanged += HandlePlayerCountChanged;
            _subscribedController.OnWaveSucceeded += HandleWaveSucceeded;
            _subscribedController.OnGameFinished += HandleGameFinished;

            SyncInitialState();
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
            _subscribedController.OnTimerUpdated -= HandleTimerUpdated;
            _subscribedController.OnPlayerCountChanged -= HandlePlayerCountChanged;
            _subscribedController.OnWaveSucceeded -= HandleWaveSucceeded;
            _subscribedController.OnGameFinished -= HandleGameFinished;
            _subscribedController = null;
        }

        /// <summary> 有効化直後に現在の GameController 状態を画面へ同期する。 </summary>
        private void SyncInitialState()
        {
            GameController controller = _subscribedController != null ? _subscribedController : _gameController;
            if (controller == null)
            {
                return;
            }

            HandlePhaseChanged(controller.CurrentPhase);
            HandleTitlePressChanged(0, GameController.TITLE_REQUIRED_PRESSES);
            HandleWaveStarted(controller.CurrentWave);
            HandleTimerUpdated(controller.WaveRemainingSeconds);
            HandlePlayerCountChanged(controller.PlayerCount, 0);
        }

        private void HandlePhaseChanged(GamePhase phase)
        {
            if (_titlePanel != null) _titlePanel.SetActive(phase == GamePhase.Title);
            if (_rulesPanel != null) _rulesPanel.SetActive(phase == GamePhase.Rules);

            // Success 表示中はゲーム画面を表示したまま成功バナーを重ねて表示する
            if (_gamePanel != null) _gamePanel.SetActive(phase == GamePhase.Playing || phase == GamePhase.Success);
            if (_resultPanel != null) _resultPanel.SetActive(phase == GamePhase.Result);
            if (_successBanner != null) _successBanner.SetActive(phase == GamePhase.Success);
        }

        private void HandleTitlePressChanged(int current, int required)
        {
            bool showProgress = required > 1;

            if (_startCountText != null)
            {
                _startCountText.gameObject.SetActive(showProgress);
                if (showProgress)
                {
                    _startCountText.text = $"{current} / {required}";
                }
            }

            if (_startMeterFill != null)
            {
                GameObject meterContainer = _startMeterFill.transform.parent != null
                    ? _startMeterFill.transform.parent.gameObject
                    : _startMeterFill.gameObject;
                meterContainer.SetActive(showProgress);

                if (showProgress)
                {
                    _startMeterFill.fillAmount = Mathf.Clamp01((float)current / required);
                }
            }
        }

        private void HandleWaveStarted(int wave)
        {
            if (_waveText != null)
            {
                _waveText.text = $"WAVE {wave:00}";
            }

            if (_feedbackText != null)
            {
                _feedbackText.text = string.Empty;
            }

            _feedbackHideTimer = 0f;
        }

        private void HandleTimerUpdated(float remainingSeconds)
        {
            if (_timerText != null)
            {
                // 仕様書第9章準拠：残り時間は小数1桁で表示
                _timerText.text = $"{remainingSeconds:0.0} SEC";
            }
        }

        private void HandlePlayerCountChanged(int currentCount, int delta)
        {
            if (_counterText != null)
            {
                // 仕様書第8章準拠：カウンターは2桁ゼロ埋め
                _counterText.text = currentCount.ToString("00");
            }

            if (_feedbackText != null && delta != 0)
            {
                _feedbackText.text = delta > 0 ? "カウントを記録した" : "ひとつ戻した";
                _feedbackHideTimer = FEEDBACK_DISPLAY_DURATION;
            }
        }

        private void HandleWaveSucceeded(int expectedCount, int nextWave)
        {
            if (_feedbackText != null)
            {
                _feedbackText.text = $"正解！（{expectedCount}回） 次のウェーブへ";
            }

            // 成功メッセージは次Waveの開始（HandleWaveStarted）まで表示し続ける
            _feedbackHideTimer = 0f;
        }

        private void HandleGameFinished(int clearedWaves, int expectedCount, int playerCount, IReadOnlyList<int> records)
        {
            if (_clearedWavesText != null)
            {
                _clearedWavesText.text = clearedWaves.ToString();
            }

            if (_resultDetailText != null)
            {
                _resultDetailText.text = $"YOUR COUNT: {playerCount:00}  /  ANSWER: {expectedCount:00}";
            }

            if (_recordValueTexts != null)
            {
                for (int i = 0; i < _recordValueTexts.Length; i++)
                {
                    if (_recordValueTexts[i] == null)
                    {
                        continue;
                    }

                    // 記録が3件未満の空欄は「—」を表示する
                    _recordValueTexts[i].text = (records != null && i < records.Count)
                        ? records[i].ToString("00")
                        : "—";
                }
            }
        }

        /// <summary>
        /// Wave切替時にUIを隠さない暗転（Canvas最背面の黒オーバーレイ）を再生する。
        /// 前半で暗転し、暗転ピークで onDarkPeak を実行して振り子・Waveを切り替え、後半で明転する。
        /// </summary>
        public async UniTask PlayWaveTransitionFadeAsync(Action onDarkPeak, CancellationToken cancellationToken)
        {
            float totalDuration = WaveFadeDurationSeconds;
            if (totalDuration <= 0f)
            {
                onDarkPeak?.Invoke();
                return;
            }

            EnsureWaveFadeOverlay();
            if (_waveFadeOverlay == null)
            {
                onDarkPeak?.Invoke();
                return;
            }

            // 必ずCanvasの最背面（SiblingIndex = 0）に配置し、全UIパネルより後ろに描画する
            _waveFadeOverlay.transform.SetAsFirstSibling();
            _waveFadeOverlay.gameObject.SetActive(true);

            float halfDuration = totalDuration * 0.5f;

            try
            {
                // 前半：0 → 1（暗転）
                await AnimateWaveFadeAsync(0f, 1f, halfDuration, cancellationToken);

                // 暗転ピークで次Waveの開始処理（振り子生成・カメラ切替）を実行
                onDarkPeak?.Invoke();

                // 後半：1 → 0（明転）
                await AnimateWaveFadeAsync(1f, 0f, halfDuration, cancellationToken);
            }
            finally
            {
                // キャンセルや終了時も暗転が残らないよう確実に透明・非表示へ戻す
                SetWaveFadeAlpha(0f);
            }
        }

        private async UniTask AnimateWaveFadeAsync(float fromAlpha, float toAlpha, float duration, CancellationToken cancellationToken)
        {
            CancelWaveFadeMotion();

            if (duration <= 0f)
            {
                SetWaveFadeAlpha(toAlpha);
                return;
            }

            SetWaveFadeAlpha(fromAlpha);

            _waveFadeMotionHandle = LMotion.Create(fromAlpha, toAlpha, duration)
                .WithEase(_waveFadeEase)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(this, static (alpha, target) => target.SetWaveFadeAlpha(alpha));

            try
            {
                await _waveFadeMotionHandle.ToUniTask(cancellationToken);
            }
            finally
            {
                CancelWaveFadeMotion();
            }
        }

        private void CancelWaveFadeMotion()
        {
            if (_waveFadeMotionHandle.IsActive())
            {
                _waveFadeMotionHandle.Cancel();
            }
        }

        /// <summary> 暗転オーバーレイが未設定の場合、Canvas先頭に全画面の黒Imageを自動生成する。 </summary>
        private void EnsureWaveFadeOverlay()
        {
            if (_waveFadeOverlay != null)
            {
                _waveFadeOverlay.raycastTarget = false;
                _waveFadeOverlay.transform.SetAsFirstSibling();
                return;
            }

            GameObject overlayObj = new GameObject("WaveFadeOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            overlayObj.transform.SetParent(transform, false);
            overlayObj.transform.SetAsFirstSibling();

            RectTransform rt = overlayObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = overlayObj.GetComponent<Image>();
            img.color = new Color(_waveFadeColor.r, _waveFadeColor.g, _waveFadeColor.b, 0f);
            img.raycastTarget = false;

            _waveFadeOverlay = img;
        }

        private void SetWaveFadeAlpha(float alpha)
        {
            if (_waveFadeOverlay == null)
            {
                return;
            }

            Color c = _waveFadeColor;
            c.a = Mathf.Clamp01(alpha);
            _waveFadeOverlay.color = c;
            _waveFadeOverlay.gameObject.SetActive(c.a > 0.001f);
        }

        private GameController ActiveController => _subscribedController != null ? _subscribedController : _gameController;

        private void HandleTitleStartButtonClicked()
        {
            ActiveController?.HandleSpaceInput();
        }

        private void HandleRulesStartButtonClicked()
        {
            ActiveController?.HandleStartGameInput();
        }

        private void HandlePlusButtonClicked()
        {
            ActiveController?.HandleSpaceInput();
        }

        private void HandleMinusButtonClicked()
        {
            ActiveController?.HandleCtrlInput();
        }

        private void HandleReturnToTitleClicked()
        {
            ActiveController?.HandleReturnToTitleInput();
        }
    }
}
