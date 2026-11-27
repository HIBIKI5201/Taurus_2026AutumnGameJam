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
        // アウトゲーム画面を離れるとき、ボタンの演出を見せるために画面を残しておく秒数
        private const float PANEL_EXIT_HOLD_SECONDS = 0.18f;

        // 残した画面を消すフェードの秒数
        private const float PANEL_EXIT_FADE_SECONDS = 0.22f;

        // 次の画面をフェードで出す秒数
        private const float PANEL_ENTER_FADE_SECONDS = 0.2f;

        // 終了ボタンを押してから実際に終了するまでの秒数（演出を見せるため）
        private const float QUIT_DELAY_SECONDS = 0.35f;

        // フィードバック文言（正解表示など）の自動非表示までの秒数
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
        [SerializeField] private Button _quitButton;
        [SerializeField] private Button _creditsButton;
        [SerializeField] private CreditsPanel _creditsPanel;

        [Header("Rules UI")]
        [SerializeField] private Button _rulesStartButton;

        [Header("Game UI")]
        [SerializeField] private Text _waveText;
        [SerializeField] private Text _timerText;
        [SerializeField] private Text _counterText;
        [SerializeField] private Text _feedbackText;
        [SerializeField] private Button _plusButton;
        [SerializeField] private Button _minusButton;

        // インゲームの少女の立ち絵（ボタンを押すたびにコマ送りで動く）
        [SerializeField] private CharacterPortrait _characterPortrait;

        [Header("Result UI")]
        [SerializeField] private Text _clearedWavesText;
        [SerializeField] private Text _resultDetailText;
        // クリアWave数の数字の色（デザイン案ではクリーム地の枠に水色の数字）
        [SerializeField] private Color _clearedWavesColor = new Color(0.29f, 0.63f, 0.76f, 1f);
        [SerializeField] private Text[] _recordValueTexts; // 要素0=1ST, 1=2ND, 2=3RD
        [SerializeField] private Button _retryButton;

        // リザルト画面左の大きな立ち絵と、ランキング更新時・非更新時の表情差分
        [SerializeField] private Image _resultCharacterImage;
        [SerializeField] private Sprite _resultRankUpdatedSprite;
        [SerializeField] private Sprite _resultRankNotUpdatedSprite;

        // リザルト画面中央の、リプレイに合わせて数え上がる答えのカウンター
        [SerializeField] private Text _replayCountText;
        [SerializeField] private RectTransform _replayCounterRoot;
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
        private MotionHandle _popMotionHandle;
        private MotionHandle _replayPopMotionHandle;
        private ButtonPressEffect _plusPressEffect;
        private ButtonPressEffect _minusPressEffect;
        private readonly Dictionary<GameObject, MotionHandle> _panelMotionHandles = new Dictionary<GameObject, MotionHandle>();
        private GamePhase? _lastPhase;
        private bool _isQuitting;

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
            CancelPopMotion();
            CancelReplayPopMotion();
            CancelPanelMotions();
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
                _plusPressEffect = _plusButton.GetComponent<ButtonPressEffect>();
            }

            if (_minusButton != null)
            {
                _minusButton.onClick.AddListener(HandleMinusButtonClicked);
                _minusPressEffect = _minusButton.GetComponent<ButtonPressEffect>();
            }

            if (_creditsButton != null)
            {
                _creditsButton.onClick.AddListener(HandleCreditsClicked);
            }

            if (_creditsPanel != null)
            {
                _creditsPanel.OnVisibilityChanged += HandleCreditsVisibilityChanged;
            }

            if (_quitButton != null)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                // WebGL（unityroom 等）では Application.Quit が機能しないため終了ボタンを非表示にする
                _quitButton.gameObject.SetActive(false);
#else
                _quitButton.onClick.AddListener(HandleQuitClicked);
#endif
            }

            if (_retryButton != null)
            {
                _retryButton.onClick.AddListener(HandleRetryClicked);
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
            _subscribedController.OnReplayCountChanged += HandleReplayCountChanged;

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
            _subscribedController.OnReplayCountChanged -= HandleReplayCountChanged;
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

        /// <summary> アウトゲーム画面（タイトル・ルール・リザルト）を消す演出にかかる秒数。 </summary>
        public float OutGameExitSeconds => PANEL_EXIT_HOLD_SECONDS + PANEL_EXIT_FADE_SECONDS;

        /// <summary>
        ///     いま表示中のアウトゲーム画面を、押されたボタンの演出つきで消し、消え終わるまで待つ。
        ///     ゲーム開始（Rules / Result → Playing）の前に GameController から呼び、画面が消えてから Wave を始めるために使う。
        /// </summary>
        public async UniTask PlayOutGameExitAsync(GamePhase nextPhase, CancellationToken cancellationToken)
        {
            if (!_lastPhase.HasValue)
            {
                return;
            }

            GameObject panel = GetOutGamePanel(_lastPhase.Value);
            if (panel == null || !panel.activeSelf || _panelMotionHandles.ContainsKey(panel))
            {
                return;
            }

            ButtonPressEffect leavingButton = GetLeavingButtonEffect(_lastPhase.Value, nextPhase);
            if (leavingButton != null)
            {
                leavingButton.Play();
            }

            PlayPanelExit(panel);
            await UniTask.Delay(TimeSpan.FromSeconds(OutGameExitSeconds), ignoreTimeScale: true, cancellationToken: cancellationToken);
        }

        private void HandlePhaseChanged(GamePhase phase)
        {
            GamePhase? previous = _lastPhase;
            _lastPhase = phase;
            bool isPhaseChanged = previous.HasValue && previous.Value != phase;

            // 前のアウトゲーム画面がまだ表示中（消す演出をしていない）なら、ここでボタン演出つきで消す
            GameObject leavingPanel = isPhaseChanged ? GetOutGamePanel(previous.Value) : null;
            bool canPlayExit = leavingPanel != null && leavingPanel.activeSelf && !_panelMotionHandles.ContainsKey(leavingPanel);

            ShowPanel(_titlePanel, phase == GamePhase.Title);
            ShowPanel(_rulesPanel, phase == GamePhase.Rules);

            // Success 表示中はゲーム画面を表示したまま成功バナーを重ねて表示する
            ShowPanel(_gamePanel, phase == GamePhase.Playing || phase == GamePhase.Success);
            ShowPanel(_resultPanel, phase == GamePhase.Result);
            if (_successBanner != null) _successBanner.SetActive(phase == GamePhase.Success);

            if (!isPhaseChanged || GetOutGamePanel(previous.Value) == null)
            {
                return;
            }

            // 前の画面が消え終わってから、次の画面をフェードで出す（同時に動くと見にくいため）
            float enterDelay = 0f;
            if (canPlayExit)
            {
                ButtonPressEffect leavingButton = GetLeavingButtonEffect(previous.Value, phase);
                if (leavingButton != null)
                {
                    leavingButton.Play();
                }

                PlayPanelExit(leavingPanel);
                enterDelay = OutGameExitSeconds;
            }

            PlayPanelEnter(GetPanelForPhase(phase), enterDelay);
        }

        private void ShowPanel(GameObject panel, bool isVisible)
        {
            if (panel == null)
            {
                return;
            }

            CancelPanelMotion(panel);
            panel.SetActive(isVisible);
        }

        private GameObject GetOutGamePanel(GamePhase phase)
        {
            switch (phase)
            {
                case GamePhase.Title: return _titlePanel;
                case GamePhase.Rules: return _rulesPanel;
                case GamePhase.Result: return _resultPanel;
                default: return null;
            }
        }

        private GameObject GetPanelForPhase(GamePhase phase)
        {
            return phase == GamePhase.Playing || phase == GamePhase.Success ? _gamePanel : GetOutGamePanel(phase);
        }

        // 画面の切り替わり方から、押されたボタンを決める
        private ButtonPressEffect GetLeavingButtonEffect(GamePhase from, GamePhase to)
        {
            Button button = null;
            if (from == GamePhase.Title && to == GamePhase.Rules) button = _titleStartButton;
            else if (from == GamePhase.Rules && to == GamePhase.Playing) button = _rulesStartButton;
            else if (from == GamePhase.Result && to == GamePhase.Playing) button = _retryButton;
            else if (from == GamePhase.Result && to == GamePhase.Title) button = _homeButton;

            return button != null ? button.GetComponent<ButtonPressEffect>() : null;
        }

        // 前の画面を少し残してからフェードで消す。その間はクリックを受け付けない
        private void PlayPanelExit(GameObject panel)
        {
            CanvasGroup group = PreparePanelGroup(panel, 1f);
            panel.SetActive(true);
            _panelMotionHandles[panel] = LMotion.Create(1f, 0f, PANEL_EXIT_FADE_SECONDS)
                .WithDelay(PANEL_EXIT_HOLD_SECONDS)
                .WithEase(Ease.InQuad)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(() =>
                {
                    _panelMotionHandles.Remove(panel);
                    ResetPanelGroup(panel);
                    panel.SetActive(false);
                })
                .Bind(group, static (alpha, g) => g.alpha = alpha);
        }

        // 次の画面を delay 秒待ってからフェードで出す。出し終わるまではクリックを受け付けない
        private void PlayPanelEnter(GameObject panel, float delay)
        {
            if (panel == null || !panel.activeSelf)
            {
                return;
            }

            CanvasGroup group = PreparePanelGroup(panel, 0f);
            _panelMotionHandles[panel] = LMotion.Create(0f, 1f, PANEL_ENTER_FADE_SECONDS)
                .WithDelay(delay)
                .WithEase(Ease.OutQuad)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .WithOnComplete(() =>
                {
                    _panelMotionHandles.Remove(panel);
                    ResetPanelGroup(panel);
                })
                .Bind(group, static (alpha, g) => g.alpha = alpha);
        }

        private static CanvasGroup PreparePanelGroup(GameObject panel, float alpha)
        {
            CanvasGroup group = panel.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = panel.AddComponent<CanvasGroup>();
            }

            group.alpha = alpha;
            group.blocksRaycasts = false;
            return group;
        }

        private void CancelPanelMotion(GameObject panel)
        {
            if (_panelMotionHandles.TryGetValue(panel, out MotionHandle handle))
            {
                _panelMotionHandles.Remove(panel);
                if (handle.IsActive())
                {
                    handle.Cancel();
                }
            }

            ResetPanelGroup(panel);
        }

        private void CancelPanelMotions()
        {
            foreach (GameObject panel in new List<GameObject>(_panelMotionHandles.Keys))
            {
                CancelPanelMotion(panel);
                panel.SetActive(_lastPhase.HasValue && GetPanelForPhase(_lastPhase.Value) == panel);
            }
        }

        private static void ResetPanelGroup(GameObject panel)
        {
            CanvasGroup group = panel.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 1f;
                group.blocksRaycasts = true;
            }
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
            if (_characterPortrait != null)
            {
                _characterPortrait.ShowIdle();
            }

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

            // キー入力・ボタンクリックのどちらでも、押されたボタンを光らせる（下限0でCtrlを押したときも押した感触は返す）
            if (delta > 0 && _plusPressEffect != null)
            {
                _plusPressEffect.Play();
            }
            else if (delta < 0 && _minusPressEffect != null)
            {
                _minusPressEffect.Play();
            }

            // カウントの数字にも、増減が見て分かる程度の控えめな演出を付ける
            if (delta != 0 && _counterText != null && _counterText.TryGetComponent(out CountTextEffect countEffect))
            {
                countEffect.Play(delta);
            }

            // 立ち絵もボタンを押すたびに「カチッ」とコマ送りで動かす
            if (delta != 0 && _characterPortrait != null)
            {
                _characterPortrait.PlayClick();
            }
        }

        private void HandleWaveSucceeded(int expectedCount, int nextWave)
        {
            if (_characterPortrait != null)
            {
                _characterPortrait.ShowClear();
            }

            // 上部バナーと下部テキストの視線分散を防ぐため、_successBanner がある場合はバナー側へ表示を統一する
            int clearedWave = Mathf.Max(1, nextWave - 1);
            Text bannerText = _successBanner != null ? _successBanner.GetComponentInChildren<Text>(true) : null;
            if (bannerText != null)
            {
                bannerText.text = L10n.Format("game.successBanner", clearedWave);
                PlayScalePop(_successBanner.transform, 0.86f, 0.24f);
                if (_feedbackText != null)
                {
                    _feedbackText.text = string.Empty;
                }
            }
            else if (_feedbackText != null)
            {
                _feedbackText.text = L10n.Format("game.successFeedback", clearedWave);
            }

            // 成功メッセージは次Waveの開始（HandleWaveStarted）まで表示し続ける
            _feedbackHideTimer = 0f;
        }

        private void HandleGameFinished(int clearedWaves, int expectedCount, int playerCount, IReadOnlyList<int> records)
        {
            bool isNewTopRecord = GameAudioHooks.IsTopRecord(clearedWaves, records);

            // ランキング（トップ3）を更新できたかで、リザルトの立ち絵の表情を変える
            if (_resultCharacterImage != null)
            {
                Sprite resultSprite = isNewTopRecord ? _resultRankUpdatedSprite : _resultRankNotUpdatedSprite;
                if (resultSprite != null)
                {
                    _resultCharacterImage.sprite = resultSprite;
                }
            }

            if (_clearedWavesText != null)
            {
                _clearedWavesText.text = clearedWaves.ToString("00");
                _clearedWavesText.color = _clearedWavesColor;
                PlayScalePop(_clearedWavesText.transform, isNewTopRecord ? 0.75f : 0.88f, 0.35f);
            }

            if (_resultDetailText != null)
            {
                // デザイン案どおり、失敗したWaveと「自分のカウント/正解」だけを出す
                int failedWave = clearedWaves + 1;
                _resultDetailText.text = $"WAVE{failedWave}\n{playerCount}/{expectedCount}";
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

        /// <summary> クリアWave数に応じた Peak-End ランク評価ラベルを返す。 </summary>
        public static string FormatResultRankLabel(int clearedWaves, bool isNewRecord)
        {
            string rank;
            if (clearedWaves >= 15)
            {
                rank = "S RANK - COMPLETE!（全15層制覇）";
            }
            else if (clearedWaves >= 10)
            {
                rank = "A RANK - MASTER!（上級領域到達）";
            }
            else if (clearedWaves >= 4)
            {
                rank = "B RANK - GREAT!（好記録）";
            }
            else if (clearedWaves >= 1)
            {
                rank = "C RANK - NICE TRY!";
            }
            else
            {
                rank = "TRY AGAIN!";
            }

            return isNewRecord ? $"★ NEW RECORD ★  {rank}" : rank;
        }

        // リプレイで真上を通過するたびに答えのカウンターを進め、数字を弾ませる
        private void HandleReplayCountChanged(int replayCount, int expectedCount)
        {
            if (_replayCountText != null)
            {
                _replayCountText.text = replayCount.ToString("00");
            }

            if (replayCount <= 0 || _replayCounterRoot == null)
            {
                return;
            }

            CancelReplayPopMotion();
            _replayCounterRoot.localScale = Vector3.one * 1.18f;
            _replayPopMotionHandle = LMotion.Create(1.18f, 1f, 0.25f)
                .WithEase(Ease.OutBack)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(_replayCounterRoot, static (scale, tr) =>
                {
                    if (tr != null)
                    {
                        tr.localScale = Vector3.one * scale;
                    }
                });
        }

        private void CancelReplayPopMotion()
        {
            if (_replayPopMotionHandle.IsActive())
            {
                _replayPopMotionHandle.Cancel();
            }
        }

        private void PlayScalePop(Transform target, float startScale, float duration)
        {
            if (target == null)
            {
                return;
            }

            CancelPopMotion();
            target.localScale = Vector3.one * startScale;
            _popMotionHandle = LMotion.Create(startScale, 1f, duration)
                .WithEase(Ease.OutBack)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(target, static (scale, tr) =>
                {
                    if (tr != null)
                    {
                        tr.localScale = Vector3.one * scale;
                    }
                });
        }

        private void CancelPopMotion()
        {
            if (_popMotionHandle.IsActive())
            {
                _popMotionHandle.Cancel();
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

        private void HandleCreditsClicked()
        {
            ButtonPressEffect effect = _creditsButton.GetComponent<ButtonPressEffect>();
            if (effect != null)
            {
                effect.Play();
            }

            _creditsPanel.Open();
        }

        // クレジット画面を開いている間は、裏のタイトル画面がキー入力で進まないようにする
        private void HandleCreditsVisibilityChanged(bool isOpen)
        {
            if (ActiveController != null)
            {
                ActiveController.SetOverlayOpen(isOpen);
            }
        }

        private void HandleQuitClicked()
        {
            if (_isQuitting)
            {
                return;
            }

            _isQuitting = true;
            ButtonPressEffect effect = _quitButton.GetComponent<ButtonPressEffect>();
            if (effect != null)
            {
                effect.Play();
            }

            QuitAfterEffectAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        // 離した演出を見せてから終了する
        private async UniTaskVoid QuitAfterEffectAsync(CancellationToken token)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(QUIT_DELAY_SECONDS), ignoreTimeScale: true, cancellationToken: token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void HandleRetryClicked()
        {
            ActiveController?.HandleRetryInput();
        }

        private void HandleReturnToTitleClicked()
        {
            ActiveController?.HandleReturnToTitleInput();
        }
    }
}
