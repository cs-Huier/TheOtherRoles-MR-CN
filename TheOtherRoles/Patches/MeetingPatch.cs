using HarmonyLib;
using Hazel;
using System.Collections.Generic;
using System.Linq;
using static TheOtherRoles.TheOtherRoles;
using static TheOtherRoles.MapOptions;
using TheOtherRoles.Objects;
using System;
using TheOtherRoles.Players;
using TheOtherRoles.Utilities;
using UnityEngine;
using Assets.CoreScripts;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles.Modules;

namespace TheOtherRoles.Patches {
    [HarmonyPatch]
    class MeetingHudPatch {
        static bool[] selections;
        static SpriteRenderer[] renderers;
        private static GameData.PlayerInfo target = null;
        private const float scale = 0.65f;
        private static TMPro.TextMeshPro swapperChargesText;
        private static PassiveButton[] swapperButtonList;
        private static TMPro.TextMeshPro swapperConfirmButtonLabel;
        private static PlayerVoteArea swapped1 = null;
        private static PlayerVoteArea swapped2 = null;

        [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CheckForEndVoting))]
        class MeetingCalculateVotesPatch {
            private static Dictionary<byte, int> CalculateVotes(MeetingHud __instance) {
                Dictionary<byte, int> dictionary = new Dictionary<byte, int>();
                for (int i = 0; i < __instance.playerStates.Length; i++) {
                    PlayerVoteArea playerVoteArea = __instance.playerStates[i];
                    if (playerVoteArea.VotedFor != 252 && playerVoteArea.VotedFor != 255 && playerVoteArea.VotedFor != 254) {
                        PlayerControl player = Helpers.playerById((byte)playerVoteArea.TargetPlayerId);
                        if (player == null || player.Data == null || player.Data.IsDead || player.Data.Disconnected) continue;

                        int currentVotes;
                        int additionalVotes = (Mayor.mayor != null && Mayor.mayor.PlayerId == playerVoteArea.TargetPlayerId) ? 2 : 1; // Mayor vote
                        if (dictionary.TryGetValue(playerVoteArea.VotedFor, out currentVotes))
                            dictionary[playerVoteArea.VotedFor] = currentVotes + additionalVotes;
                        else
                            dictionary[playerVoteArea.VotedFor] = additionalVotes;
                    }
                }
                // Swapper swap votes
                if (Swapper.swapper != null && !Swapper.swapper.Data.IsDead) {
                    swapped1 = null;
                    swapped2 = null;
                    foreach (PlayerVoteArea playerVoteArea in __instance.playerStates) {
                        if (playerVoteArea.TargetPlayerId == Swapper.playerId1) swapped1 = playerVoteArea;
                        if (playerVoteArea.TargetPlayerId == Swapper.playerId2) swapped2 = playerVoteArea;
                    }

                    if (swapped1 != null && swapped2 != null) {
                        if (!dictionary.ContainsKey(swapped1.TargetPlayerId)) dictionary[swapped1.TargetPlayerId] = 0;
                        if (!dictionary.ContainsKey(swapped2.TargetPlayerId)) dictionary[swapped2.TargetPlayerId] = 0;
                        int tmp = dictionary[swapped1.TargetPlayerId];
                        dictionary[swapped1.TargetPlayerId] = dictionary[swapped2.TargetPlayerId];
                        dictionary[swapped2.TargetPlayerId] = tmp;
                    }
                }

                return dictionary;
            }


            static bool Prefix(MeetingHud __instance) {
                if (__instance.playerStates.All((PlayerVoteArea ps) => ps.AmDead || ps.DidVote)) {
                    // If skipping is disabled, replace skipps/no-votes with self vote
                    if (target == null && blockSkippingInEmergencyMeetings && noVoteIsSelfVote) {
                        foreach (PlayerVoteArea playerVoteArea in __instance.playerStates) {
                            if (playerVoteArea.VotedFor == byte.MaxValue - 1) playerVoteArea.VotedFor = playerVoteArea.TargetPlayerId; // TargetPlayerId
                        }
                    }

                    if (YasunaJr.yasunaJr != null && !YasunaJr.yasunaJr.Data.IsDead && YasunaJr.specialVoteTargetPlayerId != byte.MaxValue)
					{
                        byte takeAwayTheVoteTargetPlayerId = YasunaJr.specialVoteTargetPlayerId;
                        for (int i = 0; i < __instance.playerStates.Length; i++)
                        {
                            PlayerVoteArea playerVoteArea = __instance.playerStates[i];
                            if (playerVoteArea.TargetPlayerId == YasunaJr.specialVoteTargetPlayerId)
                            {
                                byte selfVotedFor = byte.MaxValue;
                                for (int j = 0; j < __instance.playerStates.Length; j++)
                                {
                                    if (__instance.playerStates[j].TargetPlayerId == YasunaJr.yasunaJr.PlayerId)
                                    {
                                        selfVotedFor = __instance.playerStates[j].VotedFor;
                                        break;
                                    }
                                }
                                playerVoteArea.VotedFor = selfVotedFor;
                            }
                        }
                    }

                    Dictionary<byte, int> self = CalculateVotes(__instance);
                    bool tie;
			        KeyValuePair<byte, int> max = self.MaxPair(out tie);
                    GameData.PlayerInfo exiled = GameData.Instance.AllPlayers.ToArray().FirstOrDefault(v => !tie && v.PlayerId == max.Key && !v.IsDead);

                    // TieBreaker 
                    List<GameData.PlayerInfo> potentialExiled = new List<GameData.PlayerInfo>();
                    bool skipIsTie = false;
                    if (self.Count > 0) {
                        Tiebreaker.isTiebreak = false;
                        int maxVoteValue = self.Values.Max();
                        PlayerVoteArea tb = null;
                        if (Tiebreaker.tiebreaker != null)
                            tb = __instance.playerStates.ToArray().FirstOrDefault(x => x.TargetPlayerId == Tiebreaker.tiebreaker.PlayerId);
                        bool isTiebreakerSkip = tb == null || tb.VotedFor == 253;
                        if (tb != null && tb.AmDead) isTiebreakerSkip = true;

                        foreach (KeyValuePair<byte, int> pair in self) {
                            if (pair.Value != maxVoteValue || isTiebreakerSkip) continue;
                            if (pair.Key != 253)
                                potentialExiled.Add(GameData.Instance.AllPlayers.ToArray().FirstOrDefault(x => x.PlayerId == pair.Key));
                            else 
                                skipIsTie = true;
                        }
                    }

                    byte forceTargetPlayerId = Yasuna.yasuna != null && !Yasuna.yasuna.Data.IsDead && Yasuna.specialVoteTargetPlayerId != byte.MaxValue ? Yasuna.specialVoteTargetPlayerId : byte.MaxValue;
                    if (forceTargetPlayerId != byte.MaxValue)
                        tie = false;
                    MeetingHud.VoterState[] array = new MeetingHud.VoterState[__instance.playerStates.Length];
                    for (int i = 0; i < __instance.playerStates.Length; i++)
                    {
                        PlayerVoteArea playerVoteArea = __instance.playerStates[i];
                        if (forceTargetPlayerId != byte.MaxValue)
                            playerVoteArea.VotedFor = forceTargetPlayerId;

                        array[i] = new MeetingHud.VoterState {
                            VoterId = playerVoteArea.TargetPlayerId,
                            VotedForId = playerVoteArea.VotedFor
                        };

                        if (Tiebreaker.tiebreaker == null || playerVoteArea.TargetPlayerId != Tiebreaker.tiebreaker.PlayerId) continue;

                        byte tiebreakerVote = playerVoteArea.VotedFor;
                        if (swapped1 != null && swapped2 != null) {
                            if (tiebreakerVote == swapped1.TargetPlayerId) tiebreakerVote = swapped2.TargetPlayerId;
                            else if (tiebreakerVote == swapped2.TargetPlayerId) tiebreakerVote = swapped1.TargetPlayerId;
                        }

                        if (potentialExiled.FindAll(x => x != null && x.PlayerId == tiebreakerVote).Count > 0 && (potentialExiled.Count > 1 || skipIsTie)) {
                            exiled = potentialExiled.ToArray().FirstOrDefault(v => v.PlayerId == tiebreakerVote);
                            tie = false;

                            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(CachedPlayer.LocalPlayer.PlayerControl.NetId, (byte)CustomRPC.SetTiebreak, Hazel.SendOption.Reliable, -1);
                            AmongUsClient.Instance.FinishRpcImmediately(writer);
                            RPCProcedure.setTiebreak();
                        }
                    }

                    if (forceTargetPlayerId != byte.MaxValue)
                        exiled = GameData.Instance.AllPlayers.ToArray().FirstOrDefault(v => v.PlayerId == forceTargetPlayerId && !v.IsDead);

                    // RPCVotingComplete
                    __instance.RpcVotingComplete(array, exiled, tie);
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.BloopAVoteIcon))]
        class MeetingHudBloopAVoteIconPatch {
            public static bool Prefix(MeetingHud __instance, [HarmonyArgument(0)]GameData.PlayerInfo voterPlayer, [HarmonyArgument(1)]int index, [HarmonyArgument(2)]Transform parent) {
                SpriteRenderer spriteRenderer = UnityEngine.Object.Instantiate<SpriteRenderer>(__instance.PlayerVotePrefab);
                int cId = voterPlayer.DefaultOutfit.ColorId;
                if (!(!PlayerControl.GameOptions.AnonymousVotes || (CachedPlayer.LocalPlayer.Data.IsDead && MapOptions.ghostsSeeVotes) || Mayor.mayor != null && CachedPlayer.LocalPlayer.PlayerControl == Mayor.mayor && Mayor.canSeeVoteColors && TasksHandler.taskInfo(CachedPlayer.LocalPlayer.Data).Item1 >= Mayor.tasksNeededToSeeVoteColors))
                    voterPlayer.Object.SetColor(6);                    
                voterPlayer.Object.SetPlayerMaterialColors(spriteRenderer);
                spriteRenderer.transform.SetParent(parent);
                spriteRenderer.transform.localScale = Vector3.zero;
                __instance.StartCoroutine(Effects.Bloop((float)index * 0.3f, spriteRenderer.transform, 1f, 0.5f));
                parent.GetComponent<VoteSpreader>().AddVote(spriteRenderer);
                voterPlayer.Object.SetColor(cId);
                return false;
            }
        } 

        [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.PopulateResults))]
        class MeetingHudPopulateVotesPatch {
            
            static bool Prefix(MeetingHud __instance, Il2CppStructArray<MeetingHud.VoterState> states) {
                // Swapper swap

                PlayerVoteArea swapped1 = null;
                PlayerVoteArea swapped2 = null;
                foreach (PlayerVoteArea playerVoteArea in __instance.playerStates) {
                    if (playerVoteArea.TargetPlayerId == Swapper.playerId1) swapped1 = playerVoteArea;
                    if (playerVoteArea.TargetPlayerId == Swapper.playerId2) swapped2 = playerVoteArea;
                }
                bool doSwap = swapped1 != null && swapped2 != null && Swapper.swapper != null && !Swapper.swapper.Data.IsDead;
                if (doSwap) {
                    __instance.StartCoroutine(Effects.Slide3D(swapped1.transform, swapped1.transform.localPosition, swapped2.transform.localPosition, 1.5f));
                    __instance.StartCoroutine(Effects.Slide3D(swapped2.transform, swapped2.transform.localPosition, swapped1.transform.localPosition, 1.5f));
                }


                __instance.TitleText.text = FastDestroyableSingleton<TranslationController>.Instance.GetString(StringNames.MeetingVotingResults, new Il2CppReferenceArray<Il2CppSystem.Object>(0));
                int num = 0;
                for (int i = 0; i < __instance.playerStates.Length; i++) {
                    PlayerVoteArea playerVoteArea = __instance.playerStates[i];
                    byte targetPlayerId = playerVoteArea.TargetPlayerId;
                    // Swapper change playerVoteArea that gets the votes
                    if (doSwap && playerVoteArea.TargetPlayerId == swapped1.TargetPlayerId) playerVoteArea = swapped2;
                    else if (doSwap && playerVoteArea.TargetPlayerId == swapped2.TargetPlayerId) playerVoteArea = swapped1;

                    playerVoteArea.ClearForResults();
                    int num2 = 0;
                    bool mayorFirstVoteDisplayed = false;
                    for (int j = 0; j < states.Length; j++) {
                        MeetingHud.VoterState voterState = states[j];
                        GameData.PlayerInfo playerById = GameData.Instance.GetPlayerById(voterState.VoterId);
                        if (playerById == null) {
                            Debug.LogError(string.Format("Couldn't find player info for voter: {0}", voterState.VoterId));
                        } else if (i == 0 && voterState.SkippedVote && !playerById.IsDead) {
                            __instance.BloopAVoteIcon(playerById, num, __instance.SkippedVoting.transform);
                            num++;
                        }
                        else if (voterState.VotedForId == targetPlayerId && !playerById.IsDead) {
                            __instance.BloopAVoteIcon(playerById, num2, playerVoteArea.transform);
                            num2++;
                        }

                        // Major vote, redo this iteration to place a second vote
                        if (Mayor.mayor != null && voterState.VoterId == (sbyte)Mayor.mayor.PlayerId && !mayorFirstVoteDisplayed) {
                            mayorFirstVoteDisplayed = true;
                            j--;    
                        }
                    }
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.VotingComplete))]
        class MeetingHudVotingCompletedPatch {
            static void Postfix(MeetingHud __instance, [HarmonyArgument(0)]byte[] states, [HarmonyArgument(1)]GameData.PlayerInfo exiled, [HarmonyArgument(2)]bool tie)
            {
                if (Yasuna.isYasuna(CachedPlayer.LocalPlayer.PlayerControl.PlayerId) && Yasuna.specialVoteTargetPlayerId == byte.MaxValue) {
                    for (int i = 0; i < __instance.playerStates.Length; i++) {
                        PlayerVoteArea voteArea = __instance.playerStates[i];
                        Transform t = voteArea.transform.FindChild("SpecialVoteButton");
                        if (t != null)
                            t.gameObject.SetActive(false);
                    }
                }
                if (YasunaJr.isYasunaJr(CachedPlayer.LocalPlayer.PlayerControl.PlayerId) && YasunaJr.specialVoteTargetPlayerId == byte.MaxValue) {
                    for (int i = 0; i < __instance.playerStates.Length; i++) {
                        PlayerVoteArea voteArea = __instance.playerStates[i];
                        Transform t = voteArea.transform.FindChild("SpecialVoteButton2");
                        if (t != null)
                            t.gameObject.SetActive(false);
                    }
                }

                // Reset swapper values
                Swapper.playerId1 = Byte.MaxValue;
                Swapper.playerId2 = Byte.MaxValue;

                // Lovers, Lawyer & Pursuer save next to be exiled, because RPC of ending game comes before RPC of exiled
                Lovers.notAckedExiledIsLover = false;
                Pursuer.notAckedExiled = false;
                if (exiled != null) {
                    Lovers.notAckedExiledIsLover = ((Lovers.lover1 != null && Lovers.lover1.PlayerId == exiled.PlayerId) || (Lovers.lover2 != null && Lovers.lover2.PlayerId == exiled.PlayerId));
                    Pursuer.notAckedExiled = (Pursuer.pursuer != null && Pursuer.pursuer.PlayerId == exiled.PlayerId) || (Lawyer.lawyer != null && Lawyer.target != null && Lawyer.target.PlayerId == exiled.PlayerId && Lawyer.target != Jester.jester && !Lawyer.isProsecutor);
                }

                // Mini
                if (!Mini.isGrowingUpInMeeting) Mini.timeOfGrowthStart = Mini.timeOfGrowthStart.Add(DateTime.UtcNow.Subtract(Mini.timeOfMeetingStart));
            }
        }


        static void swapperOnClick(int i, MeetingHud __instance) {
            if (__instance.state == MeetingHud.VoteStates.Results || Swapper.charges <= 0) return;
            if (__instance.playerStates[i].AmDead) return;

            int selectedCount = selections.Where(b => b).Count();
            SpriteRenderer renderer = renderers[i];

            if (selectedCount == 0) {
                renderer.color = Color.yellow;
                selections[i] = true;
            } else if (selectedCount == 1) {
                if (selections[i]) {
                    renderer.color = Color.red;
                    selections[i] = false;
                } else {
                    selections[i] = true;
                    renderer.color = Color.yellow;
                    swapperConfirmButtonLabel.text = Helpers.cs(Color.yellow, ModTranslation.GetString("Game-Swapper", 2));
                }
            } else if (selectedCount == 2) {
                if (selections[i]) {
                    renderer.color = Color.red;
                    selections[i] = false;
                    swapperConfirmButtonLabel.text = Helpers.cs(Color.red, ModTranslation.GetString("Game-Swapper", 2));
                }
            }
        }

        static void swapperConfirm(MeetingHud __instance) {
            __instance.playerStates[0].Cancel();  // This will stop the underlying buttons of the template from showing up
            if (__instance.state == MeetingHud.VoteStates.Results) return;
            if (selections.Where(b => b).Count() != 2) return;
            if (Swapper.charges <= 0 || Swapper.playerId1 != Byte.MaxValue) return;

            PlayerVoteArea firstPlayer = null;
            PlayerVoteArea secondPlayer = null;
            for (int A = 0; A < selections.Length; A++) {
                if (selections[A]) {
                    if (firstPlayer == null) {
                        firstPlayer = __instance.playerStates[A];
                    } else {
                        secondPlayer = __instance.playerStates[A];
                    }
                    renderers[A].color = Color.green;
                } else if (renderers[A] != null) {
                    renderers[A].color = Color.gray;
                    }
                if (swapperButtonList[A] != null) swapperButtonList[A].OnClick.RemoveAllListeners();  // Swap buttons can't be clicked / changed anymore
            }
            if (firstPlayer != null && secondPlayer != null) {
                MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(CachedPlayer.LocalPlayer.PlayerControl.NetId, (byte)CustomRPC.SwapperSwap, Hazel.SendOption.Reliable, -1);
                writer.Write((byte)firstPlayer.TargetPlayerId);
                writer.Write((byte)secondPlayer.TargetPlayerId);
                AmongUsClient.Instance.FinishRpcImmediately(writer);

                RPCProcedure.swapperSwap((byte)firstPlayer.TargetPlayerId, (byte)secondPlayer.TargetPlayerId);
                swapperConfirmButtonLabel.text = Helpers.cs(Color.green, ModTranslation.GetString("Game-Swapper", 3));
                Swapper.charges--;
                swapperChargesText.text = string.Format(ModTranslation.GetString("Game-Swapper", 4), Swapper.charges);
            }
        }

        public static GameObject guesserUI;
        public static PassiveButton guesserUIExitButton;
        public static byte guesserCurrentTarget;
        static void guesserOnClick(int buttonTarget, MeetingHud __instance) {
            if (guesserUI != null || !(__instance.state == MeetingHud.VoteStates.Voted || __instance.state == MeetingHud.VoteStates.NotVoted)) return;
            __instance.playerStates.ToList().ForEach(x => x.gameObject.SetActive(false));

            Transform container = UnityEngine.Object.Instantiate(__instance.transform.FindChild("PhoneUI"), __instance.transform);
            container.transform.localPosition = new Vector3(0, 0, -5f);
            guesserUI = container.gameObject;

            int i = 0;
            var buttonTemplate = __instance.playerStates[0].transform.FindChild("votePlayerBase");
            var maskTemplate = __instance.playerStates[0].transform.FindChild("MaskArea");
            var smallButtonTemplate = __instance.playerStates[0].Buttons.transform.Find("CancelButton");
            var textTemplate = __instance.playerStates[0].NameText;

            guesserCurrentTarget = __instance.playerStates[buttonTarget].TargetPlayerId;

            Transform exitButtonParent = (new GameObject()).transform;
            exitButtonParent.SetParent(container);
            Transform exitButton = UnityEngine.Object.Instantiate(buttonTemplate.transform, exitButtonParent);
            Transform exitButtonMask = UnityEngine.Object.Instantiate(maskTemplate, exitButtonParent);
            exitButton.gameObject.GetComponent<SpriteRenderer>().sprite = smallButtonTemplate.GetComponent<SpriteRenderer>().sprite;
            exitButtonParent.transform.localPosition = new Vector3(2.725f, 2.1f, -5);
            exitButtonParent.transform.localScale = new Vector3(0.217f, 0.9f, 1);
            guesserUIExitButton = exitButton.GetComponent<PassiveButton>();
            guesserUIExitButton.OnClick.RemoveAllListeners();
            guesserUIExitButton.OnClick.AddListener((System.Action)(() => {
                __instance.playerStates.ToList().ForEach(x => {
                    x.gameObject.SetActive(true);
                    if (CachedPlayer.LocalPlayer.Data.IsDead && x.transform.FindChild("ShootButton") != null) UnityEngine.Object.Destroy(x.transform.FindChild("ShootButton").gameObject);
                });
                UnityEngine.Object.Destroy(container.gameObject);
            }));

            List<Transform> buttons = new List<Transform>();
            Transform selectedButton = null;

            foreach (RoleInfo roleInfo in RoleInfo.allRoleInfos) {
                RoleId guesserRole = (Guesser.niceGuesser != null && CachedPlayer.LocalPlayer.PlayerId == Guesser.niceGuesser.PlayerId) ? RoleId.NiceGuesser :  RoleId.EvilGuesser;
                if (roleInfo.isModifier || roleInfo.roleId == guesserRole || (!HandleGuesser.evilGuesserCanGuessSpy && guesserRole == RoleId.EvilGuesser && roleInfo.roleId == RoleId.Spy && !HandleGuesser.isGuesserGm)) continue; // Not guessable roles & modifier
                if (HandleGuesser.isGuesserGm && (roleInfo.roleId == RoleId.NiceGuesser || roleInfo.roleId == RoleId.EvilGuesser)) continue; // remove Guesser for guesser game mode
                if (HandleGuesser.isGuesserGm && CachedPlayer.LocalPlayer.PlayerControl.Data.Role.IsImpostor && !HandleGuesser.evilGuesserCanGuessSpy && roleInfo.roleId == RoleId.Spy) continue;
                // remove all roles that cannot spawn due to the settings from the ui.
                RoleManagerSelectRolesPatch.RoleAssignmentData roleData = RoleManagerSelectRolesPatch.getRoleAssignmentData();
                if (roleData.neutralSettings.ContainsKey((byte)roleInfo.roleId) && roleData.neutralSettings[(byte)roleInfo.roleId] == 0) continue;
                else if (roleData.impSettings.ContainsKey((byte)roleInfo.roleId) && roleData.impSettings[(byte)roleInfo.roleId] == 0) continue;
                else if (roleData.crewSettings.ContainsKey((byte)roleInfo.roleId) && roleData.crewSettings[(byte)roleInfo.roleId] == 0) continue;
                else if (new List<RoleId>() { RoleId.Janitor, RoleId.Godfather, RoleId.Mafioso }.Contains(roleInfo.roleId) && CustomOptionHolder.mafiaSpawnRate.getSelection() == 0) continue;
                else if (roleInfo.roleId == RoleId.Sidekick && (!CustomOptionHolder.jackalCanCreateSidekick.getBool() || CustomOptionHolder.jackalSpawnRate.getSelection() == 0)) continue;
                if (roleInfo.roleId == RoleId.Deputy && (CustomOptionHolder.deputySpawnRate.getSelection() == 0 || CustomOptionHolder.sheriffSpawnRate.getSelection() == 0)) continue;
                if (roleInfo.roleId == RoleId.Pursuer && CustomOptionHolder.lawyerSpawnRate.getSelection() == 0) continue;
                if (roleInfo.roleId == RoleId.Spy && roleData.impostors.Count <= 1) continue;
                if (roleInfo.roleId == RoleId.Prosecutor && (CustomOptionHolder.lawyerIsProsecutorChance.getSelection() == 0 || CustomOptionHolder.lawyerSpawnRate.getSelection() == 0)) continue;
                if (roleInfo.roleId == RoleId.Lawyer && (CustomOptionHolder.lawyerIsProsecutorChance.getSelection() == 10 || CustomOptionHolder.lawyerSpawnRate.getSelection() == 0)) continue;
                if (Snitch.snitch != null && HandleGuesser.guesserCantGuessSnitch) {
                    var (playerCompleted, playerTotal) = TasksHandler.taskInfo(Snitch.snitch.Data);
                    int numberOfLeftTasks = playerTotal - playerCompleted;
                    if (numberOfLeftTasks <= 0 && roleInfo.roleId == RoleId.Snitch) continue;
                }

                Transform buttonParent = (new GameObject()).transform;
                buttonParent.SetParent(container);
                Transform button = UnityEngine.Object.Instantiate(buttonTemplate, buttonParent);
                Transform buttonMask = UnityEngine.Object.Instantiate(maskTemplate, buttonParent);
                TMPro.TextMeshPro label = UnityEngine.Object.Instantiate(textTemplate, button);
                button.GetComponent<SpriteRenderer>().sprite = FastDestroyableSingleton<HatManager>.Instance.GetNamePlateById("nameplate_NoPlate")?.viewData?.viewData?.Image;
                buttons.Add(button);
                int row = i/5, col = i%5;
                buttonParent.localPosition = new Vector3(-3.47f + 1.75f * col, 1.5f - 0.45f * row, -5);
                buttonParent.localScale = new Vector3(0.55f, 0.55f, 1f);
                label.text = Helpers.cs(roleInfo.color, roleInfo.name);
                label.alignment = TMPro.TextAlignmentOptions.Center;
                label.transform.localPosition = new Vector3(0, 0, label.transform.localPosition.z);
                label.transform.localScale *= 1.7f;
                int copiedIndex = i;

                button.GetComponent<PassiveButton>().OnClick.RemoveAllListeners();
                if (!CachedPlayer.LocalPlayer.Data.IsDead && !Helpers.playerById((byte)__instance.playerStates[buttonTarget].TargetPlayerId).Data.IsDead) button.GetComponent<PassiveButton>().OnClick.AddListener((System.Action)(() => {
                    if (selectedButton != button) {
                        selectedButton = button;
                        buttons.ForEach(x => x.GetComponent<SpriteRenderer>().color = x == selectedButton ? Color.red : Color.white);
                    } else {
                        PlayerControl focusedTarget = Helpers.playerById((byte)__instance.playerStates[buttonTarget].TargetPlayerId);
                        if (!(__instance.state == MeetingHud.VoteStates.Voted || __instance.state == MeetingHud.VoteStates.NotVoted) || focusedTarget == null || HandleGuesser.remainingShots(CachedPlayer.LocalPlayer.PlayerId) <= 0 ) return;

                        if (!HandleGuesser.killsThroughShield && focusedTarget == Medic.shielded) { // Depending on the options, shooting the shielded player will not allow the guess, notifiy everyone about the kill attempt and close the window
                            __instance.playerStates.ToList().ForEach(x => x.gameObject.SetActive(true)); 
                            UnityEngine.Object.Destroy(container.gameObject);

                            MessageWriter murderAttemptWriter = AmongUsClient.Instance.StartRpcImmediately(CachedPlayer.LocalPlayer.PlayerControl.NetId, (byte)CustomRPC.ShieldedMurderAttempt, Hazel.SendOption.Reliable, -1);
                            AmongUsClient.Instance.FinishRpcImmediately(murderAttemptWriter);
                            RPCProcedure.shieldedMurderAttempt();
                            SoundEffectsManager.play("fail");
                            return;
                        }

                        var mainRoleInfo = RoleInfo.getRoleInfoForPlayer(focusedTarget, false).FirstOrDefault();
                        if (mainRoleInfo == null) return;

                        PlayerControl dyingTarget = (mainRoleInfo == roleInfo) ? focusedTarget : CachedPlayer.LocalPlayer.PlayerControl;

                        // Reset the GUI
                        __instance.playerStates.ToList().ForEach(x => x.gameObject.SetActive(true));
                        UnityEngine.Object.Destroy(container.gameObject);
                        if (HandleGuesser.hasMultipleShotsPerMeeting && HandleGuesser.remainingShots(CachedPlayer.LocalPlayer.PlayerId) > 1 && dyingTarget != CachedPlayer.LocalPlayer.PlayerControl)
                            __instance.playerStates.ToList().ForEach(x => { if (x.TargetPlayerId == dyingTarget.PlayerId && x.transform.FindChild("ShootButton") != null) UnityEngine.Object.Destroy(x.transform.FindChild("ShootButton").gameObject); });
                        else
                            __instance.playerStates.ToList().ForEach(x => { if (x.transform.FindChild("ShootButton") != null) UnityEngine.Object.Destroy(x.transform.FindChild("ShootButton").gameObject); });

                        // Shoot player and send chat info if activated
                        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(CachedPlayer.LocalPlayer.PlayerControl.NetId, (byte)CustomRPC.GuesserShoot, Hazel.SendOption.Reliable, -1);
                        writer.Write(CachedPlayer.LocalPlayer.PlayerId);
                        writer.Write(dyingTarget.PlayerId);
                        writer.Write(focusedTarget.PlayerId);
                        writer.Write((byte)roleInfo.roleId);
                        AmongUsClient.Instance.FinishRpcImmediately(writer);
                        RPCProcedure.guesserShoot(CachedPlayer.LocalPlayer.PlayerId, dyingTarget.PlayerId, focusedTarget.PlayerId, (byte)roleInfo.roleId);
                    }
                }));

                i++;
            }
            container.transform.localScale *= 0.75f;
        }

        static void yasunaOnClick(int buttonTarget, MeetingHud __instance) {
            if (Yasuna.yasuna != null && (Yasuna.yasuna.Data.IsDead || Yasuna.specialVoteTargetPlayerId != byte.MaxValue)) return;
            if (!(__instance.state == MeetingHud.VoteStates.Voted || __instance.state == MeetingHud.VoteStates.NotVoted || __instance.state == MeetingHud.VoteStates.Results)) return;
            if (__instance.playerStates[buttonTarget].AmDead) return;

            byte targetId = __instance.playerStates[buttonTarget].TargetPlayerId;
            RPCProcedure.yasunaSpecialVote(CachedPlayer.LocalPlayer.PlayerControl.PlayerId, targetId);
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(CachedPlayer.LocalPlayer.PlayerControl.NetId, (byte)CustomRPC.YasunaSpecialVote, Hazel.SendOption.Reliable, -1);
            writer.Write(CachedPlayer.LocalPlayer.PlayerControl.PlayerId);
            writer.Write(targetId);
            AmongUsClient.Instance.FinishRpcImmediately(writer);

            __instance.SkipVoteButton.gameObject.SetActive(false);
            for (int i = 0; i < __instance.playerStates.Length; i++) {
                PlayerVoteArea voteArea = __instance.playerStates[i];
                voteArea.ClearButtons();
                Transform t = voteArea.transform.FindChild("SpecialVoteButton");
                if (t != null && voteArea.TargetPlayerId != targetId)
                    t.gameObject.SetActive(false);
            }
            if (AmongUsClient.Instance.AmHost) {
                PlayerControl target = Helpers.playerById(targetId);
                if (target != null)
                    MeetingHud.Instance.CmdCastVote(CachedPlayer.LocalPlayer.PlayerControl.PlayerId, target.PlayerId);
            }
        }

        static void yasunaJrOnClick(int buttonTarget, MeetingHud __instance) {
            if (YasunaJr.yasunaJr != null && YasunaJr.yasunaJr.Data.IsDead) return;
            if (!(__instance.state == MeetingHud.VoteStates.Voted || __instance.state == MeetingHud.VoteStates.NotVoted)) return;
            if (__instance.playerStates[buttonTarget].AmDead) return;
            SoundManager.Instance.PlaySound(ModUpdateBehaviour.selectSfx, false, 1f, null).volume = 0.8f;
            for (int i = 0; i < __instance.playerStates.Length; i++)
            {
                PlayerVoteArea voteArea = __instance.playerStates[i];
                Transform t = voteArea.transform.FindChild("SpecialVoteButton2");
                if (t != null)
				{
                    var s = t.gameObject.GetComponent<SpriteRenderer>();
                    if (s != null)
                        s.color = new Color(s.color.r, s.color.g, s.color.b, i == buttonTarget ? 1.0f : 0.5f);
                }
            }

            byte targetId = __instance.playerStates[buttonTarget].TargetPlayerId;
            RPCProcedure.yasunaJrSpecialVote(CachedPlayer.LocalPlayer.PlayerControl.PlayerId, targetId);
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(CachedPlayer.LocalPlayer.PlayerControl.NetId, (byte)CustomRPC.YasunaJrSpecialVote, Hazel.SendOption.Reliable, -1);
            writer.Write(CachedPlayer.LocalPlayer.PlayerControl.PlayerId);
            writer.Write(targetId);
            AmongUsClient.Instance.FinishRpcImmediately(writer);

            /*
            __instance.SkipVoteButton.gameObject.SetActive(false);
            for (int i = 0; i < __instance.playerStates.Length; i++)
            {
                PlayerVoteArea voteArea = __instance.playerStates[i];
                voteArea.ClearButtons();
                Transform t = voteArea.transform.FindChild("SpecialVoteButton2");
                if (t != null && voteArea.TargetPlayerId != targetId)
                    t.gameObject.SetActive(false);
            }
            */

            /*
            if (AmongUsClient.Instance.AmHost)
            {
                PlayerControl target = Helpers.playerById(targetId);
                if (target != null)
                    MeetingHud.Instance.CmdCastVote(CachedPlayer.LocalPlayer.PlayerControl.PlayerId, target.PlayerId);
            }
            */
        }

        [HarmonyPatch(typeof(PlayerVoteArea), nameof(PlayerVoteArea.Select))]
        class PlayerVoteAreaSelectPatch {
            static bool Prefix(MeetingHud __instance) {
                if (CachedPlayer.LocalPlayer.PlayerControl != null) {
                    if (HandleGuesser.isGuesser(CachedPlayer.LocalPlayer.PlayerId) && guesserUI != null)
                        return false;
                    if (Yasuna.isYasuna(CachedPlayer.LocalPlayer.PlayerId) && Yasuna.specialVoteTargetPlayerId != byte.MaxValue)
                        return false;
                }

                return true;
            }
        }

        static void populateButtonsPostfix(MeetingHud __instance) {
            // Add Swapper Buttons
            if (Swapper.swapper != null && CachedPlayer.LocalPlayer.PlayerControl == Swapper.swapper && !Swapper.swapper.Data.IsDead) {
                selections = new bool[__instance.playerStates.Length];
                renderers = new SpriteRenderer[__instance.playerStates.Length];
                swapperButtonList = new PassiveButton[__instance.playerStates.Length];

                for (int i = 0; i < __instance.playerStates.Length; i++) {
                    PlayerVoteArea playerVoteArea = __instance.playerStates[i];
                    if (playerVoteArea.AmDead || (playerVoteArea.TargetPlayerId == Swapper.swapper.PlayerId && Swapper.canOnlySwapOthers)) continue;

                    GameObject template = playerVoteArea.Buttons.transform.Find("CancelButton").gameObject;
                    GameObject checkbox = UnityEngine.Object.Instantiate(template);
                    checkbox.transform.SetParent(playerVoteArea.transform);
                    checkbox.transform.position = template.transform.position;
                    checkbox.transform.localPosition = new Vector3(-0.95f, 0.03f, -1.3f);
                    if (HandleGuesser.isGuesserGm && HandleGuesser.isGuesser(CachedPlayer.LocalPlayer.PlayerId)) checkbox.transform.localPosition = new Vector3(-0.5f, 0.03f, -1.3f);
                    SpriteRenderer renderer = checkbox.GetComponent<SpriteRenderer>();
                    renderer.sprite = Swapper.getCheckSprite();
                    renderer.color = Color.red;

                    if (Swapper.charges <= 0) renderer.color = Color.gray;

                    PassiveButton button = checkbox.GetComponent<PassiveButton>();
                    swapperButtonList[i] = button;
                    button.OnClick.RemoveAllListeners();
                    int copiedIndex = i;
                    button.OnClick.AddListener((System.Action)(() => swapperOnClick(copiedIndex, __instance)));
                    
                    selections[i] = false;
                    renderers[i] = renderer;
                }

                // Add the "Confirm Swap" button and "Swaps: X" text next to it
                Transform meetingUI = __instance.transform.FindChild("PhoneUI");
                var buttonTemplate = __instance.playerStates[0].transform.FindChild("votePlayerBase");
                var maskTemplate = __instance.playerStates[0].transform.FindChild("MaskArea");
                var textTemplate = __instance.playerStates[0].NameText;
                Transform confirmSwapButtonParent = (new GameObject()).transform;
                confirmSwapButtonParent.SetParent(meetingUI);
                Transform confirmSwapButton = UnityEngine.Object.Instantiate(buttonTemplate, confirmSwapButtonParent);

                Transform infoTransform = __instance.playerStates[0].NameText.transform.parent.FindChild("Info");
                TMPro.TextMeshPro meetingInfo = infoTransform != null ? infoTransform.GetComponent<TMPro.TextMeshPro>() : null;
                swapperChargesText = UnityEngine.Object.Instantiate(__instance.playerStates[0].NameText, confirmSwapButtonParent);
                swapperChargesText.text = string.Format(ModTranslation.GetString("Game-Swapper", 4), Swapper.charges);
                swapperChargesText.enableWordWrapping = false;
                swapperChargesText.transform.localScale = Vector3.one * 1.7f;
                swapperChargesText.transform.localPosition = new Vector3(-2.5f, 0f, 0f);

                Transform confirmSwapButtonMask = UnityEngine.Object.Instantiate(maskTemplate, confirmSwapButtonParent);
                swapperConfirmButtonLabel = UnityEngine.Object.Instantiate(textTemplate, confirmSwapButton);
                confirmSwapButton.GetComponent<SpriteRenderer>().sprite = FastDestroyableSingleton<HatManager>.Instance.GetNamePlateById("nameplate_NoPlate")?.viewData?.viewData?.Image;
                confirmSwapButtonParent.localPosition = new Vector3(0, -2.225f, -5);
                confirmSwapButtonParent.localScale = new Vector3(0.55f, 0.55f, 1f);
                swapperConfirmButtonLabel.text = Helpers.cs(Color.red, ModTranslation.GetString("Game-Swapper", 2));
                swapperConfirmButtonLabel.alignment = TMPro.TextAlignmentOptions.Center;
                swapperConfirmButtonLabel.transform.localPosition = new Vector3(0, 0, swapperConfirmButtonLabel.transform.localPosition.z);
                swapperConfirmButtonLabel.transform.localScale *= 1.7f;

                PassiveButton passiveButton = confirmSwapButton.GetComponent<PassiveButton>();
                passiveButton.OnClick.RemoveAllListeners();               
                if (!CachedPlayer.LocalPlayer.Data.IsDead) passiveButton.OnClick.AddListener((Action)(() => swapperConfirm(__instance)));
                confirmSwapButton.parent.gameObject.SetActive(false);
                __instance.StartCoroutine(Effects.Lerp(7.27f, new Action<float>((p) => { // Button appears delayed, so that its visible in the voting screen only!
                    if (p == 1f) {
                        confirmSwapButton.parent.gameObject.SetActive(true);
                    }
                })));
            }

            //Fix visor in Meetings 
            /**
            foreach (PlayerVoteArea pva in __instance.playerStates) {
                if(pva.PlayerIcon != null && pva.PlayerIcon.VisorSlot != null){
                    pva.PlayerIcon.VisorSlot.transform.position += new Vector3(0, 0, -1f);
                }
            } */

            // Add overlay for spelled players
            if (Witch.witch != null && Witch.futureSpelled != null) {
                foreach (PlayerVoteArea pva in __instance.playerStates) {
                    if (Witch.futureSpelled.Any(x => x.PlayerId == pva.TargetPlayerId)) {
                        SpriteRenderer rend = (new GameObject()).AddComponent<SpriteRenderer>();
                        rend.transform.SetParent(pva.transform);
                        rend.gameObject.layer = pva.Megaphone.gameObject.layer;
                        rend.transform.localPosition = new Vector3(-0.5f, -0.03f, -1f);
                        rend.sprite = Witch.getSpelledOverlaySprite();
                    }
                }
            }

            // Add Guesser Buttons
            bool isGuesser = HandleGuesser.isGuesser(CachedPlayer.LocalPlayer.PlayerId);
            int remainingShots = HandleGuesser.remainingShots(CachedPlayer.LocalPlayer.PlayerId);

            if (isGuesser && !CachedPlayer.LocalPlayer.Data.IsDead && remainingShots > 0) {
                for (int i = 0; i < __instance.playerStates.Length; i++) {
                    PlayerVoteArea playerVoteArea = __instance.playerStates[i];
                    if (playerVoteArea.AmDead || playerVoteArea.TargetPlayerId == CachedPlayer.LocalPlayer.PlayerId) continue;
                    if (CachedPlayer.LocalPlayer != null && CachedPlayer.LocalPlayer.PlayerControl == Eraser.eraser && Eraser.alreadyErased.Contains(playerVoteArea.TargetPlayerId)) continue;

                    GameObject template = playerVoteArea.Buttons.transform.Find("CancelButton").gameObject;
                    GameObject targetBox = UnityEngine.Object.Instantiate(template, playerVoteArea.transform);
                    targetBox.name = "ShootButton";
                    targetBox.transform.localPosition = new Vector3(-0.95f, 0.03f, -1.3f);
                    SpriteRenderer renderer = targetBox.GetComponent<SpriteRenderer>();
                    renderer.sprite = HandleGuesser.getTargetSprite();
                    PassiveButton button = targetBox.GetComponent<PassiveButton>();
                    button.OnClick.RemoveAllListeners();
                    int copiedIndex = i;
                    button.OnClick.AddListener((System.Action)(() => guesserOnClick(copiedIndex, __instance)));
                }
            }

            // Add Yasuna Special Buttons
            if (Yasuna.isYasuna(CachedPlayer.LocalPlayer.PlayerControl.PlayerId) && !Yasuna.yasuna.Data.IsDead && Yasuna.remainingSpecialVotes() > 0) {
                for (int i = 0; i < __instance.playerStates.Length; i++) {
                    PlayerVoteArea playerVoteArea = __instance.playerStates[i];
                    if (playerVoteArea.AmDead || playerVoteArea.TargetPlayerId == CachedPlayer.LocalPlayer.PlayerControl.PlayerId) continue;

                    GameObject template = playerVoteArea.Buttons.transform.Find("CancelButton").gameObject;
                    GameObject targetBox = UnityEngine.Object.Instantiate(template, playerVoteArea.transform);
                    targetBox.name = "SpecialVoteButton";
                    targetBox.transform.localPosition = new Vector3(-0.95f, 0.03f, -2.5f);
                    SpriteRenderer renderer = targetBox.GetComponent<SpriteRenderer>();
                    renderer.sprite = Yasuna.getTargetSprite(CachedPlayer.LocalPlayer.PlayerControl.Data.Role.IsImpostor);
                    PassiveButton button = targetBox.GetComponent<PassiveButton>();
                    button.OnClick.RemoveAllListeners();
                    int copiedIndex = i;
                    button.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => yasunaOnClick(copiedIndex, __instance)));

                    TMPro.TextMeshPro targetBoxRemainText = UnityEngine.Object.Instantiate(__instance.playerStates[0].NameText, targetBox.transform);
                    targetBoxRemainText.text = Yasuna.remainingSpecialVotes().ToString();
                    targetBoxRemainText.color = CachedPlayer.LocalPlayer.PlayerControl.Data.Role.IsImpostor ? Palette.ImpostorRed : Yasuna.color;
                    targetBoxRemainText.alignment = TMPro.TextAlignmentOptions.Center;
                    targetBoxRemainText.transform.localPosition = new Vector3(0.2f, -0.3f, targetBoxRemainText.transform.localPosition.z);
                    targetBoxRemainText.transform.localScale *= 1.7f;
                }
            }

            // Add Yasuna Jr. Special Buttons
            if (YasunaJr.isYasunaJr(CachedPlayer.LocalPlayer.PlayerControl.PlayerId) && !YasunaJr.yasunaJr.Data.IsDead && YasunaJr.remainingSpecialVotes() > 0) {
                for (int i = 0; i < __instance.playerStates.Length; i++) {
                    PlayerVoteArea playerVoteArea = __instance.playerStates[i];
                    if (playerVoteArea.AmDead || playerVoteArea.TargetPlayerId == CachedPlayer.LocalPlayer.PlayerControl.PlayerId) continue;

                    GameObject template = playerVoteArea.Buttons.transform.Find("CancelButton").gameObject;
                    GameObject targetBox = UnityEngine.Object.Instantiate(template, playerVoteArea.transform);
                    targetBox.name = "SpecialVoteButton2";
                    targetBox.transform.localPosition = new Vector3(-0.95f, 0.03f, -2.5f);
                    SpriteRenderer renderer = targetBox.GetComponent<SpriteRenderer>();
                    renderer.sprite = YasunaJr.getTargetSprite(CachedPlayer.LocalPlayer.PlayerControl.Data.Role.IsImpostor);
                    renderer.color = new Color(renderer.color.r, renderer.color.g, renderer.color.b, 0.5f);
                    PassiveButton button = targetBox.GetComponent<PassiveButton>();
                    button.OnClick.RemoveAllListeners();
                    int copiedIndex = i;
                    button.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => yasunaJrOnClick(copiedIndex, __instance)));

                    TMPro.TextMeshPro targetBoxRemainText = UnityEngine.Object.Instantiate(__instance.playerStates[0].NameText, targetBox.transform);
                    targetBoxRemainText.text = YasunaJr.remainingSpecialVotes().ToString();
                    targetBoxRemainText.color = YasunaJr.color;
                    targetBoxRemainText.alignment = TMPro.TextAlignmentOptions.Center;
                    targetBoxRemainText.transform.localPosition = new Vector3(0.2f, -0.3f, targetBoxRemainText.transform.localPosition.z);
                    targetBoxRemainText.transform.localScale *= 1.7f;
                }
            }
        }

        [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.ServerStart))]
        class MeetingServerStartPatch {
            static void Postfix(MeetingHud __instance)
            {
                //populateButtonsPostfix(__instance);
            }
        }

        [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Deserialize))]
        class MeetingDeserializePatch {
            static void Postfix(MeetingHud __instance, [HarmonyArgument(0)]MessageReader reader, [HarmonyArgument(1)]bool initialState)
            {
				// Add swapper buttons
				//if (initialState) {
				//	populateButtonsPostfix(__instance);
				//}
			}
        }

        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.StartMeeting))]
        class StartMeetingPatch {
            public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)]GameData.PlayerInfo meetingTarget) {
                // MOD追加処理
                {
                    // Reset Bait list
                    Bait.active = new Dictionary<DeadPlayer, float>();
                    // Save AntiTeleport position, if the player is able to move (i.e. not on a ladder or a gap thingy)
                    if (CachedPlayer.LocalPlayer.PlayerPhysics.enabled && CachedPlayer.LocalPlayer.PlayerControl.moveable || CachedPlayer.LocalPlayer.PlayerControl.inVent
                        || HudManagerStartPatch.hackerVitalsButton.isEffectActive || HudManagerStartPatch.hackerAdminTableButton.isEffectActive || HudManagerStartPatch.securityGuardCamButton.isEffectActive
                        || Portal.isTeleporting && Portal.teleportedPlayers.Last().playerId == CachedPlayer.LocalPlayer.PlayerId)
                    {
                        AntiTeleport.position = CachedPlayer.LocalPlayer.transform.position;
                    }

                    // Medium meeting start time
                    Medium.meetingStartTime = DateTime.UtcNow;
                    // Mini
                    Mini.timeOfMeetingStart = DateTime.UtcNow;
                    Mini.ageOnMeetingStart = Mathf.FloorToInt(Mini.growingProgress() * 18);
                    // Reset vampire bitten
                    Vampire.bitten = null;
                    // Count meetings
                    if (meetingTarget == null) meetingsCount++;
                    // Save the meeting target
                    target = meetingTarget;

                    // Add Portal info into Portalmaker Chat:
                    if (Portalmaker.portalmaker != null && CachedPlayer.LocalPlayer.PlayerControl == Portalmaker.portalmaker && !CachedPlayer.LocalPlayer.Data.IsDead)
                    {
                        foreach (var entry in Portal.teleportedPlayers)
                        {
                            float timeBeforeMeeting = ((float)(DateTime.UtcNow - entry.time).TotalMilliseconds) / 1000;
                            string msg = Portalmaker.logShowsTime ? string.Format(ModTranslation.GetString("Game-Portalmaker", 1), (int)timeBeforeMeeting) : "";
                            msg = msg + string.Format(ModTranslation.GetString("Game-Portalmaker", 2), entry.name);
                            FastDestroyableSingleton<HudManager>.Instance.Chat.AddChat(CachedPlayer.LocalPlayer.PlayerControl, $"{msg}");
                        }
                    }

                    // Add trapped Info into Trapper chat
                    if (Trapper.trapper != null && CachedPlayer.LocalPlayer.PlayerControl == Trapper.trapper)
                    {
                        foreach (Trap trap in Trap.traps)
                        {
                            if (!trap.revealed) continue;
                            string message = string.Format(ModTranslation.GetString("Game-Trapper", 1), trap.instanceId);
                            trap.trappedPlayer = trap.trappedPlayer.OrderBy(x => rnd.Next()).ToList();
                            foreach (PlayerControl p in trap.trappedPlayer)
                            {
                                if (Trapper.infoType == 0) message += RoleInfo.GetRolesString(p, false, false, false) + "\n";
                                else if (Trapper.infoType == 1)
                                {
                                    if (Helpers.isNeutral(p) || p.Data.Role.IsImpostor) message += ModTranslation.GetString("Game-Trapper", 2);
                                    else message += ModTranslation.GetString("Game-Trapper", 3);
                                }
                                else message += p.Data.PlayerName + "\n";
                            }
                            FastDestroyableSingleton<HudManager>.Instance.Chat.AddChat(CachedPlayer.LocalPlayer.PlayerControl, $"{message}");
                        }
                    }

                    Trapper.playersOnMap = new List<PlayerControl>();

                    // Remove revealed traps
                    Trap.clearRevealedTraps();

                    // Reset zoomed out ghosts
                    Helpers.toggleZoom(reset: true);

                    // Stop all playing sounds
                    SoundEffectsManager.stopAll();
                }

                // 既存処理の移植
                {
                    bool flag = target == null;
                    DestroyableSingleton<Telemetry>.Instance.WriteMeetingStarted(flag);
                    StartMeeting(__instance, target); // 変更部分
                    if (__instance.AmOwner)
                    {
                        if (flag)
                        {
                            __instance.RemainingEmergencies--;
                            StatsManager.Instance.IncrementStat(StringNames.StatsEmergenciesCalled);
                            return false;
                        }
                        StatsManager.Instance.IncrementStat(StringNames.StatsBodiesReported);
                    }
                }

                return false;
            }

            static void StartMeeting(PlayerControl reporter, GameData.PlayerInfo target)
            {
                MapUtilities.CachedShipStatus.StartCoroutine(CoStartMeeting(reporter, target).WrapToIl2Cpp());
            }

            static IEnumerator CoStartMeeting(PlayerControl reporter, GameData.PlayerInfo target)
            {
                // 既存処理の移植
                {
                    while (!MeetingHud.Instance)
                    {
                        yield return null;
                    }
                    MeetingRoomManager.Instance.RemoveSelf();
                    foreach (var p in CachedPlayer.AllPlayers)
                    {
                        if (p.PlayerControl != null)
                            p.PlayerControl.ResetForMeeting();
                    }
                    if (MapBehaviour.Instance)
                    {
                        MapBehaviour.Instance.Close();
                    }
                    if (Minigame.Instance)
                    {
                        Minigame.Instance.ForceClose();
                    }
                    MapUtilities.CachedShipStatus.OnMeetingCalled();
                    KillAnimation.SetMovement(reporter, true);
                }

                // 遅延処理追加そのままyield returnで待ちを入れるとロックしたのでHudManagerのコルーチンとして実行させる
                DestroyableSingleton<HudManager>._instance.StartCoroutine(CoStartMeeting2(reporter, target).WrapToIl2Cpp());
                yield break;
            }

            static IEnumerator CoStartMeeting2(PlayerControl reporter, GameData.PlayerInfo target)
            {
                // Modで追加する遅延処理
                {
                    // ボタンと同時に通報が入った場合のバグ対応、他のクライアントからキルイベントが飛んでくるのを待つ
                    // 見えては行けないものが見えるので暗転させる
                    MeetingHud.Instance.state = MeetingHud.VoteStates.Animating; //ゲッサーのキル用meetingupdateが呼ばれないようにするおまじない（呼ばれるとバグる）
                    HudManager hudManager = DestroyableSingleton<HudManager>.Instance;
                    var blackscreen = UnityEngine.Object.Instantiate(hudManager.FullScreen, hudManager.transform);
                    var greyscreen = UnityEngine.Object.Instantiate(hudManager.FullScreen, hudManager.transform);
                    blackscreen.color = Palette.Black;
                    blackscreen.transform.position = Vector3.zero;
                    blackscreen.transform.localPosition = new Vector3(0f, 0f, -910f);
                    blackscreen.transform.localScale = new Vector3(10f, 10f, 1f);
                    blackscreen.gameObject.SetActive(true);
                    blackscreen.enabled = true;
                    greyscreen.color = Palette.Black;
                    greyscreen.transform.position = Vector3.zero;
                    greyscreen.transform.localPosition = new Vector3(0f, 0f, -920f);
                    greyscreen.transform.localScale = new Vector3(10f, 10f, 1f);
                    greyscreen.gameObject.SetActive(true);
                    greyscreen.enabled = true;
                    TMPro.TMP_Text text;
                    RoomTracker roomTracker = FastDestroyableSingleton<HudManager>.Instance?.roomTracker;
                    var textObj = UnityEngine.Object.Instantiate(roomTracker.gameObject);
                    UnityEngine.Object.DestroyImmediate(textObj.GetComponent<RoomTracker>());
                    textObj.transform.SetParent(FastDestroyableSingleton<HudManager>.Instance.transform);
                    textObj.transform.localPosition = new Vector3(0, 0, -930f);
                    textObj.transform.localScale = Vector3.one * 5f;
                    text = textObj.GetComponent<TMPro.TMP_Text>();
                    yield return Effects.Lerp(delay, new Action<float>((p) =>
                    { // Delayed action
                        greyscreen.color = new Color(1.0f, 1.0f, 1.0f, 0.5f - p / 2);
                        string message = (delay - (p * delay)).ToString("0.00");
                        if (message == "0") return;
                        string prefix = "<color=#FFFF00FF>";
                        text.text = ModTranslation.GetString("Game-General", 13) + prefix + message + "</color>";
                        if (text != null) text.color = Color.white;
                    }));

                    text.enabled = false;
                    blackscreen.transform.SetParent(MeetingHud.Instance.transform);
                    blackscreen.transform.position = Vector3.zero;
                    blackscreen.transform.localPosition = new Vector3(0f, 0f, 9f);
                    blackscreen.transform.localScale = new Vector3(200f, 100f, 1f);
                    blackscreen.transform.SetAsFirstSibling();
                    greyscreen.transform.SetParent(MeetingHud.Instance.transform);
                    greyscreen.transform.position = Vector3.zero;
                    greyscreen.transform.localPosition = new Vector3(0f, 0f, 9f);
                    greyscreen.transform.localScale = new Vector3(200f, 100f, 1f);
                    greyscreen.transform.SetAsFirstSibling();
                    // yield return new WaitForSeconds(2f);

                    //UnityEngine.Object.Destroy(textObj);
                    //UnityEngine.Object.Destroy(blackscreen);
                    //UnityEngine.Object.Destroy(greyscreen);

                    // ミーティング画面の並び替えを直す
                    //populateButtons(MeetingHud.Instance, reporter.Data.PlayerId);
                    populateButtonsPostfix(MeetingHud.Instance);
                }

                // 既存処理の移植
                {
                    DeadBody[] array = UnityEngine.Object.FindObjectsOfType<DeadBody>();
                    GameData.PlayerInfo[] deadBodies = (from b in array
                                                        select GameData.Instance.GetPlayerById(b.ParentId)).ToArray<GameData.PlayerInfo>();
                    for (int j = 0; j < array.Length; j++)
                    {
                        if (array[j] != null && array[j].gameObject != null)
                        {
                            UnityEngine.Object.Destroy(array[j].gameObject);
                        }
                        else
                        {
                            Debug.LogError("Encountered a null Dead Body while destroying.");
                        }
                    }
                    ShapeshifterEvidence[] array2 = UnityEngine.Object.FindObjectsOfType<ShapeshifterEvidence>();
                    for (int k = 0; k < array2.Length; k++)
                    {
                        if (array2[k] != null && array2[k].gameObject != null)
                        {
                            UnityEngine.Object.Destroy(array2[k].gameObject);
                        }
                        else
                        {
                            Debug.LogError("Encountered a null Evidence while destroying.");
                        }
                    }
                    MeetingHud.Instance.StartCoroutine(MeetingHud.Instance.CoIntro(reporter.Data, target, deadBodies));
                }
                yield break;
            }

            static float delay { get { return CustomOptionHolder.delayBeforeMeeting.getFloat(); } }
        }

        [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Update))]
        class MeetingHudUpdatePatch {
            static void Postfix(MeetingHud __instance) {
                // Deactivate skip Button if skipping on emergency meetings is disabled
                if (target == null && blockSkippingInEmergencyMeetings)
                    __instance.SkipVoteButton.gameObject.SetActive(false);

                if (__instance.state >= MeetingHud.VoteStates.Discussion)
                {
                    // Remove first kill shield
                    MapOptions.firstKillPlayer = null;
                }
            }
        }

        public static void populateButtons(MeetingHud __instance, byte reporter)
        {
            // 会議に参加しないPlayerControlを持つRoleが増えたらこのListに追加
            // 特殊なplayerInfo.Role.Roleを設定することで自動的に無視できないか？もしくはフラグをplayerInfoのどこかに追加
            var playerControlesToBeIgnored = new List<PlayerControl>() {};
            playerControlesToBeIgnored.RemoveAll(x => x == null);
            var playerIdsToBeIgnored = playerControlesToBeIgnored.Select(x => x.PlayerId);
            // Generate PlayerVoteAreas
            __instance.playerStates = new PlayerVoteArea[GameData.Instance.PlayerCount - playerIdsToBeIgnored.Count()];
            int playerStatesCounter = 0;
            for (int i = 0; i < __instance.playerStates.Length + playerIdsToBeIgnored.Count(); i++)
            {
                if (playerIdsToBeIgnored.Contains(GameData.Instance.AllPlayers[i].PlayerId))
                {
                    continue;
                }
                GameData.PlayerInfo playerInfo = GameData.Instance.AllPlayers[i];
                PlayerVoteArea playerVoteArea = __instance.playerStates[playerStatesCounter] = __instance.CreateButton(playerInfo);
                playerVoteArea.Parent = __instance;
                playerVoteArea.SetTargetPlayerId(playerInfo.PlayerId);
                playerVoteArea.SetDead(reporter == playerInfo.PlayerId, playerInfo.Disconnected || playerInfo.IsDead, playerInfo.Role.Role == RoleTypes.GuardianAngel);
                playerVoteArea.UpdateOverlay();
                playerStatesCounter++;
            }
            foreach (PlayerVoteArea playerVoteArea2 in __instance.playerStates)
            {
                ControllerManager.Instance.AddSelectableUiElement(playerVoteArea2.PlayerButton, false);
            }
            __instance.SortButtons();
        }
    }
}
