﻿/*
 * Copyright (c) 2013-2015, SteamDB. All rights reserved.
 * Use of this source code is governed by a BSD-style license that can be
 * found in the LICENSE file.
 */
using System;
using SteamKit2;
using System.IO;
using System.Threading;

namespace SteamDatabaseBackend
{
    class Connection : SteamHandler
    {
        private readonly string SentryFile;
        private string AuthCode;

        public Connection(CallbackManager manager)
            : base(manager)
        {
            SentryFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sentry.bin");

            manager.Register(new Callback<SteamClient.ConnectedCallback>(OnConnected));
            manager.Register(new Callback<SteamClient.DisconnectedCallback>(OnDisconnected));
            manager.Register(new Callback<SteamUser.LoggedOnCallback>(OnLoggedOn));
            manager.Register(new Callback<SteamUser.LoggedOffCallback>(OnLoggedOff));
            manager.Register(new Callback<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth));
        }

        private void OnConnected(SteamClient.ConnectedCallback callback)
        {
            if (callback.Result != EResult.OK)
            {
                GameCoordinator.UpdateStatus(0, callback.Result.ToString());

                IRC.Instance.SendEmoteAnnounce("failed to connect: {0}", callback.Result);

                Log.WriteInfo("Steam", "Could not connect: {0}", callback.Result);

                return;
            }

            GameCoordinator.UpdateStatus(0, EResult.NotLoggedOn.ToString());

            Log.WriteInfo("Steam", "Connected, logging in...");

            byte[] sentryHash = null;

            if (File.Exists(SentryFile))
            {
                sentryHash = CryptoHelper.SHAHash(File.ReadAllBytes(SentryFile));
            }

            Steam.Instance.User.LogOn(new SteamUser.LogOnDetails
            {
                Username = Settings.Current.Steam.Username,
                Password = Settings.Current.Steam.Password,

                AuthCode = AuthCode,
                SentryFileHash = sentryHash
            });
        }

        private void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            if (!Steam.Instance.IsRunning)
            {
                Application.Instance.Timer.Stop();

                Log.WriteInfo("Steam", "Disconnected from Steam");

                return;
            }

            if (Application.Instance.Timer.Enabled)
            {
                IRC.Instance.SendMain("Disconnected from Steam. See{0} http://steamstat.us", Colors.DARKBLUE);
            }

            Application.Instance.Timer.Stop();

            GameCoordinator.UpdateStatus(0, EResult.NoConnection.ToString());

            JobManager.CancelChatJobsIfAny();

            const uint RETRY_DELAY = 15;

            Log.WriteInfo("Steam", "Disconnected from Steam. Retrying in {0} seconds...", RETRY_DELAY);

            IRC.Instance.SendEmoteAnnounce("disconnected from Steam. Retrying in {0} seconds...", RETRY_DELAY);

            Thread.Sleep(TimeSpan.FromSeconds(RETRY_DELAY));

            Steam.Instance.Client.Connect();
        }

        private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            GameCoordinator.UpdateStatus(0, callback.Result.ToString());

            if (callback.Result == EResult.AccountLogonDenied)
            {
                Console.Write("STEAM GUARD! Please enter the auth code sent to the email at {0}: ", callback.EmailDomain);

                AuthCode = Console.ReadLine().Trim();

                return;
            }

            if (callback.Result != EResult.OK)
            {
                Log.WriteInfo("Steam", "Failed to login: {0}", callback.Result);

                IRC.Instance.SendEmoteAnnounce("failed to log in: {0}", callback.Result);

                Thread.Sleep(TimeSpan.FromSeconds(2));

                return;
            }

            Log.WriteInfo("Steam", "Logged in, current Valve time is {0}", callback.ServerTime.ToString("R"));

            IRC.Instance.SendMain("Logged in to Steam. Valve time: {0}{1}", Colors.DARKGRAY, callback.ServerTime.ToString("R"));
            IRC.Instance.SendEmoteAnnounce("logged in.");

            if (Settings.IsFullRun)
            {
                if (Steam.Instance.PICSChanges.PreviousChangeNumber == 1)
                {
                    Steam.Instance.Apps.PICSGetChangesSince(1, true, true);
                }
            }
            else
            {
                JobManager.RestartJobsIfAny();

                Application.Instance.Timer.Start();
            }
        }

        private void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            Application.Instance.Timer.Stop();

            Log.WriteInfo("Steam", "Logged out of Steam: {0}", callback.Result);

            IRC.Instance.SendMain("Logged out of Steam: {0}{1}{2}. See{3} http://steamstat.us", Colors.OLIVE, callback.Result, Colors.NORMAL, Colors.DARKBLUE);
            IRC.Instance.SendEmoteAnnounce("logged out of Steam: {0}", callback.Result);

            GameCoordinator.UpdateStatus(0, callback.Result.ToString());
        }

        private void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback)
        {
            Log.WriteInfo("Steam", "Updating sentry file...");

            byte[] sentryHash = CryptoHelper.SHAHash(callback.Data);

            File.WriteAllBytes(SentryFile, callback.Data);

            Steam.Instance.User.SendMachineAuthResponse(new SteamUser.MachineAuthDetails
            {
                JobID = callback.JobID,

                FileName = callback.FileName,

                BytesWritten = callback.BytesToWrite,
                FileSize = callback.Data.Length,
                Offset = callback.Offset,

                Result = EResult.OK,
                LastError = 0,

                OneTimePassword = callback.OneTimePassword,

                SentryFileHash = sentryHash
            });
        }
    }
}
