using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using EasyUI.Toast;
using Mingle.Dev.KSK_Test._02.Scripts.Utility;
using UnityEngine;
using Mingle.Dev.Scripts.UI;

namespace Mingle.Dev.KSK_Test._02.Scripts.Muster
{
    /// <summary>
    /// 소집 관련 알림을 처리하는 서비스 컴포넌트
    /// EmoticonManager GameObject에 부착된 컴포넌트
    /// </summary>
    public class MusterNotificationService : MonoBehaviour
    {
        #region Constants
        private const float NotificationDuration = 0.2f;
        private const float ThinPopupTimeLimit = 10f;
        private const string MusterChannel = "notification_channel";
        #endregion

        #region Singleton
        private static MusterNotificationService _instance;

        public static MusterNotificationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("MusterNotificationService");
                    _instance = go.AddComponent<MusterNotificationService>();
                    DontDestroyOnLoad(go);
                }

                return _instance;
            }
        }
        #endregion

        #region Notification Methods
        public void ShowSenderNotification(string targetNickname)
        {
            string message = LocalizationManager.GetLocalizedTextWithColoredText(
                "ToastUITable",
                "ToastUI_Emo_ComeHere_Sender_Send",
                targetNickname,
                "<color=#73E9D0>{0}</color>님에게 소집 요청을 보냈어요!",
                "#73E9D0"
            );
            
            Toast.Show(message, MingleColor.BgPurpleDark, ToastPosition.TopCenter);
        }

        public void ShowReceiverNotification(string senderNickname, string senderId, string targetScene, string thumbnailUrl = null)
        {
            // * 최대 5글자 ... 만 보이게
            var nickname = TruncateString(senderNickname, 5);
            //var scenename = TruncateString(targetScene, 5);
            var sceneName = GetDisplaySceneName(targetScene);
            
            string title = LocalizationManager.GetLocalizedTextWithMultipleColors(
                "ToastUITable",
                "ToastUI_Emo_ComeHere_Receiver_Receive",
                "{0}님이 있는 {1}으로 갈까요?",
                (nickname, "#73E9D0"),
                (sceneName, "#D3BFF5")
            );

            string moveButtonText = LocalizationManager.GetLocalizedText(
                "GeneralTable",
                "UI_Common_Go",
                "이동"
            );

            string confirmButtonText = LocalizationManager.GetLocalizedText(
                "GeneralTable",
                "UI_Common_Decline",
                "거절"
            );
            ThinPopup.Create()
                .SetTitle(title)
                .SetDuration(NotificationDuration)
                .SetTimeLimit(ThinPopupTimeLimit)
                .SetConfirmAction(() => { MingleUtilities.HandleTeleport(senderNickname, senderId).Forget(); })
                .SetConfirmButtonText(confirmButtonText)
                .SetTimeoutAction(() => ShowRejectMessage(senderNickname))
                .SetThumbnailUrl(thumbnailUrl)
                .SetConfirmButtonText(moveButtonText)
                .Show();
        }

        public void ShowRequestRejectedMessage(string rejectorNickname)
        {
            string message = LocalizationManager.GetLocalizedTextWithColoredText(
                "ToastUITable",
                "ToastUI_Emo_ComeHere_Sender_Denie",
                rejectorNickname,
                "<color=#73E9D0>{0}</color>님이 바빠서 올 수 없대요",
                "#73E9D0"
            );
            
            Toast.Show(message, MingleColor.BgPurpleDark, ToastPosition.TopCenter);
        }
        
        // ! 안쓰는 거 지워도 될까요?
        public void ShowOfflinePushNotificationMessage(string targetNickname, string thumbImageUrl)
        {
            string message = LocalizationManager.GetLocalizedTextWithColoredText(
                "ToastUITable",
                "ToastUI_Offline_Push_Notification",
                targetNickname,
                "<color=#7636E2>{0}</color>님이 오프라인이어서 푸시 알림을 보냈어요.",
                "#7636E2"
            );
            
            ThinPopup.Create()
                .SetTitle(message)
                .SetDuration(0f)
                .SetTimeLimit(3f)
                .SetThumbnailUrl(thumbImageUrl)
                .Show();
        }
        #endregion

        #region Network Methods
        public async void ShowRejectMessage(string senderNickname)
        {
            var message = new Dictionary<string, string>
            {
                { "type", "muster_reject" },
                { "senderNickname", senderNickname },
                { "rejectorNickname", Information.Nickname },
                { "thumbnail", Information.CloseThumb }
            };

            await PNManager.instance.SendMessageOnChannelWithoutAPI(MusterChannel, message, null);
        }
        #endregion

        #region Utility Methods
        public static string GetDisplaySceneName(string sceneName)
        {
            if (System.Enum.TryParse<SceneName>(sceneName, out var sceneEnum))
            {
                // 로컬라이제이션된 장소 이름 반환
                switch (sceneEnum)
                {
                    case SceneName.TestSquare2Scene: 
                        return LocalizationManager.GetLocalizedText(
                            "GeneralTable",
                            "UI_Common_World",
                            "광장"
                        );
                    case SceneName.RoomEditScene: 
                        return LocalizationManager.GetLocalizedText(
                            "GeneralTable",
                            "UI_Common_Agit",
                            "아지트"
                        );
                    case SceneName.PersonalAgit: 
                        return LocalizationManager.GetLocalizedText(
                            "GeneralTable",
                            "UI_Common_Room",
                            "방"
                        );
                }
            }

            return "어딘가";
        }
        
        // 글자 자르기
        public string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }
        #endregion
    }
}
