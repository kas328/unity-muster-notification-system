using System;
using System.Collections.Generic;
using UnityEngine;
using Firebase;
using Firebase.Messaging;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

public class FCMManager : MonoBehaviour
{
    #region Singleton
    private static FCMManager _instance;

    public static FCMManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var obj = FindObjectOfType<FCMManager>();
                if (obj == null)
                {
                    obj = new GameObject("FCMManager").AddComponent<FCMManager>();
                }

                _instance = obj;
            }

            return _instance;
        }
    }
    #endregion

    #region Properties & Constants
    // 푸시 데이터 수신 이벤트 추가
    public static event Action<Dictionary<string, string>> OnPushDataReceived;
    
    // 토큰 재시도 관련 상수
    private int _tokenRetryCount = 0;
    private const int MaxRetryCount = 5; // 5초 동안
    private const int RetryDelayMS = 1000; // 1초 간격

    private readonly Dictionary<string, DateTime> _processedMessageIds = new Dictionary<string, DateTime>();
    private const double MessageExpiryMinutes = 1; // 1분 후에는 같은 ID도 새 메시지로 취급
    
    // 알림 관련 상수
    private const string NOTIFICATION_CHANNEL_ID = "mingle_notification_channel";
    private const int NOTIFICATION_ID_BASE = 1000;
    private static int _notificationIdCounter = NOTIFICATION_ID_BASE;
    
    public static string Token { get; private set; } = "";
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            CreateNotificationChannel();
            InitializeFcmAsync().Forget();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    // 비활성화 상태에서의 알림 클릭 처리를 위한 메서드 추가
    private void Start()
    {
        // 앱이 시작될 때 저장된 알림 데이터 확인
        string storedData = PlayerPrefs.GetString("FCM_DATA", "");
        
        if (!string.IsNullOrEmpty(storedData))
        {
            try
            {
                var data = JsonConvert.DeserializeObject<Dictionary<string, string>>(storedData);
                if (data != null && data.Count > 0)
                {
                    Debug.Log($"앱 시작 시 저장된 알림 데이터 발견: {storedData}");
                    
                    // 이벤트 발생
                    OnPushDataReceived?.Invoke(data);
                    
                    // 이벤트 발생 후 데이터 초기화
                    PlayerPrefs.SetString("FCM_DATA", "");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"저장된 데이터 파싱 오류: {ex.Message}");
            }
        }
    }
    
    private void OnDestroy()
    {
        FirebaseMessaging.TokenReceived -= OnTokenReceived;
        FirebaseMessaging.MessageReceived -= OnMessageReceived;
    }
    #endregion

    #region Firebase Initialization
    private async UniTaskVoid InitializeFcmAsync()
    {
        Debug.Log("Firebase 초기화 시작");

        try
        {
            var dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();

            if (dependencyStatus == DependencyStatus.Available)
            {
                Debug.Log("Firebase 초기화 성공!");

#if UNITY_IOS
                try
                {
                    await FirebaseMessaging.RequestPermissionAsync();
                    Debug.Log("알림 권한 요청 완료");
                }
                catch (Exception permissionEx)
                {
                    Debug.LogError($"알림 권한 요청 실패: {permissionEx}");
                }
#endif

                // FCM 토큰 및 메시지 수신 이벤트 등록
                FirebaseMessaging.TokenReceived += OnTokenReceived;
                FirebaseMessaging.MessageReceived += OnMessageReceived;

                // 토큰 가져오기 시도
                await TryGetFcmTokenAsync();
                
                //await CheckForInitialNotificationAsync();
            }
            else
            {
                Debug.LogError($"Firebase 초기화 실패: {dependencyStatus}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Firebase 초기화 중 예외 발생: {ex}");
        }
    }
    
    // 초기 알림 확인
    private async UniTask CheckForInitialNotificationAsync()
    {
        try
        {
            // 알림 클릭으로 앱이 실행된 경우를 확인하기 위한 지연
            await UniTask.Delay(500); // 약간의 지연을 두어 Firebase가 초기화 완료되도록 함
        
            // 알림 데이터가 이미 저장되어 있는지 확인
            string storedData = PlayerPrefs.GetString("FCM_DATA", "");
            
            if (string.IsNullOrEmpty(storedData))
            {
                Debug.Log("저장된 알림 데이터 없음, 초기 알림 확인 중...");
            }
            else
            {
                Debug.Log($"저장된 알림 데이터 발견: {storedData}");
                
                try
                {
                    var data = JsonConvert.DeserializeObject<Dictionary<string, string>>(storedData);
                    if (data != null && data.Count > 0)
                    {
                        // 이벤트 발생
                        OnPushDataReceived?.Invoke(data);
                        
                        // 이벤트 발생 후 데이터 초기화
                        PlayerPrefs.SetString("FCM_DATA", "");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"초기 알림 데이터 파싱 오류: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"초기 알림 확인 중 오류 발생: {ex}");
        }
    }
    #endregion

    #region Token Management
    // FCM 토큰 발급 재시도 로직
    private async UniTask TryGetFcmTokenAsync()
    {
        _tokenRetryCount = 0;
        
        while (_tokenRetryCount < MaxRetryCount)
        {
            try
            {
                string token = await FirebaseMessaging.GetTokenAsync();
                Debug.Log($"현재 FCM 토큰: {token}");
                Token = token;
                await UpdateFcmTokenAsync(token);
                return;
            }
            catch (Exception tokenEx)
            {
                Debug.Log($"토큰 가져오기 실패 ({_tokenRetryCount + 1}/{MaxRetryCount}): {tokenEx}");
                _tokenRetryCount++;
                
                if (_tokenRetryCount < MaxRetryCount)
                {
                    await UniTask.Delay(RetryDelayMS);
                }
            }
        }
        Debug.Log("FCM 토큰 발급 최대 시도 횟수 초과");
    }

    private void OnTokenReceived(object sender, TokenReceivedEventArgs token)
    {
        Debug.Log($"새로운 FCM 토큰 발급: {token.Token}");
        Token = token.Token;
        UpdateFcmTokenAsync(token.Token).Forget();
    }

    private async UniTask<bool> UpdateFcmTokenAsync(string fcmToken)
    {
        try
        {
            bool success = await APIManager.UpdateFcmToken(APIManager.SpecifyToken(), fcmToken);
            if (success)
            {
                Debug.Log("FCM 토큰 업데이트 성공");
                return true;
            }
            else
            {
                Debug.LogError("FCM 토큰 업데이트 실패");
                return false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"FCM 토큰 업데이트 중 에러 발생: {e}");
            return false;
        }
    }
    #endregion

    #region Android_Notification_Channel
    private void CreateNotificationChannel()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Application.platform == RuntimePlatform.Android)
        {
            try
            {
                // AndroidJavaClass와 AndroidJavaObject를 사용하여 알림 채널 생성
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject context = currentActivity.Call<AndroidJavaObject>("getApplicationContext"))
                {
                    // NotificationManager 가져오기
                    AndroidJavaClass notificationManagerClass = new AndroidJavaClass("android.app.NotificationManager");
                    using (AndroidJavaObject notificationManager =
                           context.Call<AndroidJavaObject>("getSystemService", "notification"))
                    {
                        // Android API 레벨 체크 (26 이상인 경우에만 채널 생성)
                        AndroidJavaClass versionClass = new AndroidJavaClass("android.os.Build$VERSION");
                        int sdkInt = versionClass.GetStatic<int>("SDK_INT");

                        if (sdkInt >= 26)
                        {
                            // 알림 채널 생성
                            AndroidJavaObject channel = new AndroidJavaObject(
                                "android.app.NotificationChannel",
                                NOTIFICATION_CHANNEL_ID,
                                "Mingle 알림",
                                notificationManagerClass.GetStatic<int>("IMPORTANCE_DEFAULT")
                            );

                            // 채널 설정
                            channel.Call("setDescription", "Mingle 앱의 푸시 알림 채널입니다");
                            channel.Call("enableVibration", true);
                            channel.Call("setShowBadge", true);

                            // 알림 관리자에 채널 등록
                            notificationManager.Call("createNotificationChannel", channel);

                            Debug.Log("알림 채널 생성 완료: " + NOTIFICATION_CHANNEL_ID);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("알림 채널 생성 실패: " + e.Message);
            }
        }
#endif
    }
    #endregion

    #region Android Foreground Notification
    private void ShowForegroundNotification(string title, string body, Dictionary<string, string> data = null)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (Application.platform == RuntimePlatform.Android)
        {
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject context = currentActivity.Call<AndroidJavaObject>("getApplicationContext"))
                {
                    // NotificationManager 가져오기
                    using (AndroidJavaObject notificationManager = 
                           context.Call<AndroidJavaObject>("getSystemService", "notification"))
                    {
                        // PendingIntent 생성 (알림 클릭 시 앱 열기)
                        AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent", 
                            context, currentActivity.Call<AndroidJavaClass>("getClass"));
                        
                        intent.Call<AndroidJavaObject>("setFlags", 
                            intent.GetStatic<int>("FLAG_ACTIVITY_NEW_TASK") | 
                            intent.GetStatic<int>("FLAG_ACTIVITY_CLEAR_TOP"));

                        // 데이터 추가 (있는 경우)
                        if (data != null && data.Count > 0)
                        {
                            foreach (var kvp in data)
                            {
                                intent.Call<AndroidJavaObject>("putExtra", kvp.Key, kvp.Value);
                            }
                        }

                        // PendingIntent 생성
                        AndroidJavaClass pendingIntentClass = new AndroidJavaClass("android.app.PendingIntent");
                        int uniqueId = _notificationIdCounter++;
                        
                        // Android 버전에 따른 PendingIntent 플래그 설정
                        AndroidJavaClass versionClass = new AndroidJavaClass("android.os.Build$VERSION");
                        int sdkInt = versionClass.GetStatic<int>("SDK_INT");
                        int flags = pendingIntentClass.GetStatic<int>("FLAG_UPDATE_CURRENT");
                        
                        if (sdkInt >= 23) // Android 6.0 이상
                        {
                            flags |= pendingIntentClass.GetStatic<int>("FLAG_IMMUTABLE");
                        }

                        using (AndroidJavaObject pendingIntent = pendingIntentClass.CallStatic<AndroidJavaObject>(
                            "getActivity", context, uniqueId, intent, flags))
                        {
                            // NotificationCompat.Builder 생성
                            AndroidJavaClass builderClass = new AndroidJavaClass("androidx.core.app.NotificationCompat$Builder");
                            using (AndroidJavaObject builder = new AndroidJavaObject(
                                "androidx.core.app.NotificationCompat$Builder", context, NOTIFICATION_CHANNEL_ID))
                            {
                                // 알림 내용 설정
                                builder.Call<AndroidJavaObject>("setContentTitle", title ?? "");
                                builder.Call<AndroidJavaObject>("setContentText", body ?? "");
                                builder.Call<AndroidJavaObject>("setContentIntent", pendingIntent);
                                builder.Call<AndroidJavaObject>("setAutoCancel", true);
                                builder.Call<AndroidJavaObject>("setPriority", 
                                    builderClass.GetStatic<int>("PRIORITY_DEFAULT"));

                                // 앱 아이콘 설정 (기본 Unity 아이콘 사용)
                                try
                                {
                                    int iconId = context.Call<AndroidJavaObject>("getResources")
                                        .Call<int>("getIdentifier", "app_icon", "mipmap", 
                                            context.Call<string>("getPackageName"));
                                    
                                    if (iconId == 0) // 기본 아이콘이 없으면 Android 시스템 아이콘 사용
                                    {
                                        iconId = 17301651; // android.R.drawable.ic_dialog_info
                                    }
                                    
                                    builder.Call<AndroidJavaObject>("setSmallIcon", iconId);
                                }
                                catch (Exception iconEx)
                                {
                                    Debug.LogWarning($"알림 아이콘 설정 실패: {iconEx.Message}");
                                    builder.Call<AndroidJavaObject>("setSmallIcon", 17301651); // 기본 아이콘 사용
                                }

                                // 알림 표시
                                using (AndroidJavaObject notification = builder.Call<AndroidJavaObject>("build"))
                                {
                                    notificationManager.Call("notify", uniqueId, notification);
                                    Debug.Log($"포그라운드 알림 표시 완료 (ID: {uniqueId})");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"포그라운드 알림 표시 실패: {e.Message}");
                Debug.LogError($"스택 트레이스: {e.StackTrace}");
            }
        }
#endif
    }
    #endregion

    #region Notification Handling
    private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
    {
        try
        {
            Debug.Log("Firebase 메시지 수신");

            // 메시지 중복 처리 로직
            if (e?.Message == null || string.IsNullOrEmpty(e.Message.MessageId))
            {
                Debug.LogError("메시지 또는 이벤트 인자가 null입니다");
                return;
            }

            string messageId = e.Message.MessageId;
            DateTime currentTime = DateTime.UtcNow;
            bool isDuplicate = false;

            // 중복 메시지 확인
            lock (_processedMessageIds)
            {
                // 만료된 메시지 ID 제거
                List<string> expiredIds = new List<string>();
                foreach (var pair in _processedMessageIds)
                {
                    if ((currentTime - pair.Value).TotalMinutes > MessageExpiryMinutes)
                    {
                        expiredIds.Add(pair.Key);
                    }
                }

                foreach (var id in expiredIds)
                {
                    _processedMessageIds.Remove(id);
                }

                if (expiredIds.Count > 0)
                {
                    Debug.Log($"{expiredIds.Count}개의 만료된 메시지 ID 제거됨");
                }

                // 중복 체크
                if (_processedMessageIds.TryGetValue(messageId, out DateTime lastTime))
                {
                    double minutesSinceLastMessage = (currentTime - lastTime).TotalMinutes;

                    if (minutesSinceLastMessage < MessageExpiryMinutes)
                    {
                        isDuplicate = true;
                        Debug.Log($"중복 메시지 감지 (ID: {messageId}, 마지막 처리 후 경과 시간: {minutesSinceLastMessage:F2}분)");
                    }
                    else
                    {
                        // 만료 시간 이후에는 같은 ID라도 새로운 메시지로 처리
                        Debug.Log($"만료된 ID 재사용 감지 (ID: {messageId}, 경과 시간: {minutesSinceLastMessage:F2}분)");
                        _processedMessageIds[messageId] = currentTime; // 시간 업데이트
                    }
                }
                else
                {
                    // 새 메시지 ID 추가
                    _processedMessageIds[messageId] = currentTime;
                }
            }

            if (isDuplicate) return;

            // 알림 정보 추출
            string notificationTitle = "";
            string notificationBody = "";
            
            if (e.Message.Notification != null)
            {
                notificationTitle = e.Message.Notification.Title ?? "";
                notificationBody = e.Message.Notification.Body ?? "";
            }

            Dictionary<string, string> safeData = new Dictionary<string, string>();

            // 데이터가 있을 경우 안전하게 복사
            if (e.Message.Data != null && e.Message.Data.Count > 0)
            {
                foreach (var key in e.Message.Data.Keys)
                {
                    if (key == null) continue;
                    string value = e.Message.Data[key];
                    safeData[key] = value ?? "null"; // 값이 null이면 "null" 문자열로 대체
                }

                // 데이터에서 제목과 본문을 추출할 수도 있음
                if (string.IsNullOrEmpty(notificationTitle) && safeData.TryGetValue("title", out var value1))
                {
                    notificationTitle = value1;
                }
                if (string.IsNullOrEmpty(notificationBody) && safeData.TryGetValue("body", out var value2))
                {
                    notificationBody = value2;
                }
            }

            // 포그라운드 알림 표시 (Android에서만)
            if (!string.IsNullOrEmpty(notificationTitle) || !string.IsNullOrEmpty(notificationBody))
            {
                ShowForegroundNotification(notificationTitle, notificationBody, safeData);
            }

            // 기존 데이터 처리 로직
            if (safeData.Count > 0)
            {
                try
                {
                    // JsonSerializerSettings로 null 처리 설정
                    var settings = new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        MissingMemberHandling = MissingMemberHandling.Ignore,
                        Error = (_, args) =>
                        {
                            Debug.LogError($"JSON 오류: {args.ErrorContext.Error.Message}");
                            args.ErrorContext.Handled = true;
                        }
                    };

                    var jsonData = JsonConvert.SerializeObject(safeData, settings);

                    // 데이터 저장
                    if (!string.IsNullOrEmpty(jsonData) && jsonData != "{}")
                    {
                        PlayerPrefs.SetString("FCM_DATA", jsonData);
                        PlayerPrefs.Save();
                        Debug.Log("FCM 데이터 PlayerPrefs에 저장 완료: " + jsonData);

                        OnPushDataReceived?.Invoke(safeData);

                        // 처리 완료 후 데이터 초기화
                        PlayerPrefs.SetString("FCM_DATA", "");
                        PlayerPrefs.Save();
                    }
                }
                catch (Exception jsonEx)
                {
                    Debug.LogError($"JSON 직렬화 오류: {jsonEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"메시지 처리 중 예외 발생: {ex.Message}");
            Debug.LogError($"스택 트레이스: {ex.StackTrace}");
        }
    }
    #endregion
    
    #region Cleanup Methods
    /// <summary>
    /// 로그아웃 시 FCM 관련 데이터 정리
    /// </summary>
    public async UniTask ClearFcmData()
    {
        try
        {
            Debug.Log("FCM 데이터 정리 시작");
        
            // 토큰 초기화
            await APIManager.UpdateFcmToken(APIManager.SpecifyToken(), "");
            Token = "";
        
            // 저장된 FCM 데이터 삭제
            PlayerPrefs.DeleteKey("FCM_DATA");
            PlayerPrefs.Save();
        
            // 처리된 메시지 ID 목록 정리
            lock (_processedMessageIds)
            {
                _processedMessageIds.Clear();
            }
        
            // 토큰 재시도 횟수 초기화
            _tokenRetryCount = 0;
        
            Debug.Log("FCM 데이터 정리 완료");
        }
        catch (Exception ex)
        {
            Debug.LogError($"FCM 데이터 정리 중 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 새로운 사용자 로그인을 위한 FCM 재초기화
    /// </summary>
    public async UniTask ReinitializeForNewUser()
    {
        try
        {
            Debug.Log("새 사용자를 위한 FCM 재초기화 시작");
        
            // 기존 데이터 정리
            await ClearFcmData();
        
            // 새 토큰 발급 시도
            await TryGetFcmTokenAsync();
        
            Debug.Log("새 사용자를 위한 FCM 재초기화 완료");
        }
        catch (Exception ex)
        {
            Debug.LogError($"FCM 재초기화 중 오류: {ex.Message}");
        }
    }
    #endregion
}
