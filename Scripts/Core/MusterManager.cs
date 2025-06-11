using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using PubnubApi;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mingle.Dev.KSK_Test._02.Scripts.Muster
{
    public class MusterManager : MonoBehaviour
    {
        #region Properties & Fields
        public static MusterManager Instance { get; private set; }

        private const string MusterChannel = "notification_channel";
        private readonly Dictionary<string, CancellationTokenSource> _musterTimers = new Dictionary<string, CancellationTokenSource>();
        private readonly HashSet<string> _musteredUsers = new HashSet<string>();
        public event Action<string> OnMusterTimerExpired;
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            if (PNManager.instance != null)
            {
                PNManager.instance.SubscribeChannel(MusterChannel);
                PNManager.instance.listener.onMessage += OnPubNubMessage;
            }
        }

        private void OnDestroy()
        {
            if (PNManager.instance != null)
            {
                PNManager.instance.listener.onMessage -= OnPubNubMessage;
                PNManager.instance.UnsubscribeChannel(MusterChannel);
            }

            foreach (var cts in _musterTimers.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _musterTimers.Clear();
        }
        #endregion

        #region Muster Request Methods
        public async UniTask SendMusterRequest(string targetNickname, string userId, string thumbImageUrl)
        {
            // * 소집하기 요청
            try
            {
                _musteredUsers.Add(targetNickname);
                MusterNotificationService.Instance.ShowSenderNotification(targetNickname);

                var presence = await PNManager.instance.GetUserPresenceAsync(new List<string> { userId });
                bool isOnline = presence[userId];

                if (!isOnline)
                {
                    string currentScene = SceneManager.GetActiveScene().name;
                    string displayLocation = MusterNotificationService.GetDisplaySceneName(currentScene);
                    await APIManager.SendMusterPushNotification(APIManager.SpecifyToken(), userId, displayLocation);
                    return;
                }

                var message = new Dictionary<string, string>
                {
                    { "type", "muster_request" },
                    { "senderNickname", Information.Nickname },
                    { "senderId", Information.UserId },
                    { "targetNickname", targetNickname },
                    { "thumbnail", Information.CloseThumb },
                    { "scene", SceneManager.GetActiveScene().name },
                };

                await PNManager.instance.SendMessageOnChannelWithoutAPI(MusterChannel, message, null);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to send muster request: {e.Message}\n{e.StackTrace}");
                _musteredUsers.Remove(targetNickname);
                throw;
            }
        }

        private void OnMusterRequestReceived(string senderNickname, string senderId, string targetScene, string thumbnailImage)
        {
            MusterNotificationService.Instance.ShowReceiverNotification(senderNickname, senderId, targetScene, thumbnailImage);
        }
        #endregion

        #region Notification Handling
        private async void OnPubNubMessage(Pubnub pn, PNMessageResult<object> result)
        {
            // * 소집하기 받은 사람
            if (result.Channel != MusterChannel) return;

            var message = JsonConvert.DeserializeObject<Dictionary<string, string>>(result.Message.ToString());
            if (message == null) return;

            switch (message["type"])
            {
                case "muster_request":
                    if (message["targetNickname"] == Information.Nickname)
                    {
                        string senderNickname = message["senderNickname"];
                        string senderId = message["senderId"];
                        string targetScene = message["scene"];
                        string thumbnailUrl = message.GetValueOrDefault("thumbnail");

                        OnMusterRequestReceived(senderNickname, senderId, targetScene, thumbnailUrl);
                    }
                    break;

                case "muster_reject":
                    if (message["senderNickname"] == Information.Nickname)
                    {
                        string rejectorNickname = message["rejectorNickname"];
                        string thumbnailUrl = message.GetValueOrDefault("thumbnail");

                        MusterNotificationService.Instance.ShowRequestRejectedMessage(rejectorNickname);
                    }
                    break;
            }
        }
        #endregion

        #region Timer Management
        public void StartMusterTimer(string nickname)
        {
            CancelMusterTimer(nickname);
            var cts = new CancellationTokenSource();
            _musterTimers[nickname] = cts;

            UniTask.Delay(System.TimeSpan.FromSeconds(180), cancellationToken: cts.Token)
                .ContinueWith(() =>
                {
                    _musteredUsers.Remove(nickname);
                    _musterTimers.Remove(nickname);
                    OnMusterTimerExpired?.Invoke(nickname);
                })
                .Forget();
        }

        public void CancelMusterTimer(string nickname)
        {
            if (_musterTimers.TryGetValue(nickname, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _musterTimers.Remove(nickname);
            }
        }

        public bool IsMustered(string nickname) => _musteredUsers.Contains(nickname);
        #endregion
    }
}
