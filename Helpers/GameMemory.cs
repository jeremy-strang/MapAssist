/**
 *   Copyright (C) 2021 okaygo
 *
 *   https://github.com/misterokaygo/MapAssist/
 *
 *  This program is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with this program.  If not, see <https://www.gnu.org/licenses/>.
 **/

using MapAssist.Settings;
using MapAssist.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace MapAssist.Helpers
{
    public static class GameMemory
    {

        private static readonly NLog.Logger _log = NLog.LogManager.GetCurrentClassLogger();
        private static Dictionary<int, uint> _lastMapSeeds = new Dictionary<int, uint>();
        private static Dictionary<int, bool> _playerMapChanged = new Dictionary<int, bool>();
        private static Dictionary<int, uint> _playerCubeOwnerID = new Dictionary<int, uint>();
        private static Dictionary<int, Area> _playerArea = new Dictionary<int, Area>();
        private static Dictionary<int, Session> _sessions = new Dictionary<int, Session>();
        private static int _currentProcessId;

        public static Dictionary<int, UnitPlayer> PlayerUnits = new Dictionary<int, UnitPlayer>();
        public static Dictionary<int, Dictionary<string, UnitPlayer>> Corpses = new Dictionary<int, Dictionary<string, UnitPlayer>>();
        public static Dictionary<object, object> cache = new Dictionary<object, object>();

        private static bool _firstMemoryRead = true;
        private static bool _errorThrown = false;

        public static GameData GetGameData()
        {
            //if (!MapAssistConfiguration.Loaded.RenderingConfiguration.StickToLastGameWindow && !GameManager.IsGameInForeground)
            //{
            //    return null;
            //}

            var processContext = GameManager.GetProcessContext();

            if (processContext == null)
            {
                return null;
            }

            var inGame = false;
            using (processContext)
            {
                _currentProcessId = processContext.ProcessId;

                var menuOpen = processContext.Read<byte>(GameManager.MenuOpenOffset);
                var menuData = processContext.Read<Structs.MenuData>(GameManager.MenuDataOffset);
                var lastHoverData = processContext.Read<Structs.HoverData>(GameManager.LastHoverDataOffset);
                //var lastNpcInteracted = (Npc)processContext.Read<ushort>(GameManager.InteractedNpcOffset);
                var rosterData = new Roster(GameManager.RosterDataOffset);
                inGame = menuData.InGame;
                if (!menuData.InGame)
                {
                    if (_sessions.ContainsKey(_currentProcessId))
                    {
                        _sessions.Remove(_currentProcessId);
                    }

                    if (_playerArea.ContainsKey(_currentProcessId))
                    {
                        _playerArea.Remove(_currentProcessId);
                    }

                    if (_lastMapSeeds.ContainsKey(_currentProcessId))
                    {
                        _lastMapSeeds.Remove(_currentProcessId);
                    }

                    if (Corpses.ContainsKey(_currentProcessId))
                    {
                        Corpses[_currentProcessId].Clear();
                    }

                    return null;
                }

                if (!_sessions.ContainsKey(_currentProcessId))
                {
                    _sessions.Add(_currentProcessId, new Session(GameManager.GameNameOffset));
                }

                var rawPlayerUnits = GetUnits<UnitPlayer>(UnitType.Player).Select(x => x.Update()).Where(x => x != null).ToArray();
                var playerUnit = rawPlayerUnits.FirstOrDefault(x => x.IsPlayer && x.IsPlayerUnit);

                if (playerUnit == null)
                {
                    if (_errorThrown) return null;

                    _errorThrown = true;
                    throw new Exception("Player unit not found.");
                }
                _errorThrown = false;

                if (!PlayerUnits.ContainsKey(_currentProcessId))
                {
                    PlayerUnits.Add(_currentProcessId, playerUnit);
                }
                else 
                {
                    PlayerUnits[_currentProcessId] = playerUnit;
                }

                var levelId = playerUnit.Area;

                if (!levelId.IsValid())
                {
                    if (_errorThrown) return null;

                    _errorThrown = true;
                    throw new Exception("Level id out of bounds.");
                }

                // Update area timer
                var areaCacheFound = _playerArea.TryGetValue(_currentProcessId, out var previousArea);
                if (!areaCacheFound || previousArea != levelId)
                {
                    if (areaCacheFound)
                    {
                        _sessions[_currentProcessId].TotalAreaTimeElapsed[previousArea] = _sessions[_currentProcessId].AreaTimeElapsed;
                    }

                    _playerArea[_currentProcessId] = levelId;

                    if (areaCacheFound)
                    {
                        _sessions[_currentProcessId].LastAreaChange = DateTime.Now;
                        _sessions[_currentProcessId].PreviousAreaTime = _sessions[_currentProcessId].TotalAreaTimeElapsed.TryGetValue(levelId, out var previousTime) ? previousTime : 0d;
                    }
                }

                // Check for map seed
                var mapSeedData = new MapSeed(GameManager.MapSeedOffset);
                var mapSeed = mapSeedData.Seed;

                if (mapSeed <= 0 || mapSeed > 0xFFFFFFFF)
                {
                    if (_errorThrown) return null;

                    _errorThrown = true;
                    throw new Exception("Map seed is out of bounds.");
                }

                // Check if exited the game
                if (!_lastMapSeeds.ContainsKey(_currentProcessId))
                {
                    _lastMapSeeds.Add(_currentProcessId, 0);
                }

                if (!_playerMapChanged.ContainsKey(_currentProcessId))
                {
                    _playerMapChanged.Add(_currentProcessId, false);
                }

                if (!_playerCubeOwnerID.ContainsKey(_currentProcessId))
                {
                    _playerCubeOwnerID.Add(_currentProcessId, uint.MaxValue);
                }

                // Check if new game
                if (mapSeed == _lastMapSeeds[_currentProcessId])
                {
                    _playerMapChanged[_currentProcessId] = false;
                }
                else
                {
                    UpdateMemoryData();
                    _lastMapSeeds[_currentProcessId] = mapSeed;
                    _playerMapChanged[_currentProcessId] = true;
                }

                // Extra checks on game details
                var gameDifficulty = playerUnit.Act.ActMisc.GameDifficulty;

                if (!gameDifficulty.IsValid())
                {
                    if (_errorThrown) return null;

                    _errorThrown = true;
                    throw new Exception("Game difficulty out of bounds.");
                }

                var areaLevel = levelId.Level(gameDifficulty);

                // Players
                var playerList = rawPlayerUnits.Where(x => x.UnitType == UnitType.Player && x.IsPlayer)
                    .Select(x => x.UpdateRosterEntry(rosterData)).ToArray()
                    .Select(x => x.UpdateParties(playerUnit.RosterEntry)).ToArray()
                    .Where(x => x != null && x.UnitId < uint.MaxValue).ToDictionary(x => x.UnitId, x => x);

                // Monsters
                var rawMonsterUnits = GetUnits<UnitMonster>(UnitType.Monster)
                    .Select(x => x.Update()).ToArray()
                    .Where(x => x != null && x.UnitId < uint.MaxValue).ToArray();

                var monsterList = rawMonsterUnits.Where(x => x.UnitType == UnitType.Monster && x.IsMonster).ToArray();
                var mercList = rawMonsterUnits.Where(x => x.UnitType == UnitType.Monster && x.IsMerc).ToArray();

                // Return data
                _firstMemoryRead = false;
                _errorThrown = false;

                return new GameData
                {
                    InGame = inGame,
                    PlayerPosition = playerUnit.Position,
                    MapSeed = mapSeed,
                    Area = levelId,
                    Difficulty = gameDifficulty,
                    MainWindowHandle = GameManager.MainWindowHandle,
                    PlayerName = playerUnit.Name,
                    PlayerUnit = playerUnit,
                    Players = playerList,
                    Corpses = new UnitPlayer[0] { },
                    Monsters = monsterList,
                    Mercs = mercList,
                    Objects = new UnitObject[0] { },
                    Missiles = new UnitMissile[0] { },
                    Items = new UnitItem[0] { },
                    AllItems = new UnitItem[0] { },
                    ItemLog = new ItemLogEntry[0] { },
                    Session = _sessions[_currentProcessId],
                    Roster = rosterData,
                    MenuOpen = menuData,
                    MenuPanelOpen = menuOpen,
                    LastNpcInteracted = Npc.Skeleton,
                    ProcessId = _currentProcessId
                };
            }
        }

        public static UnitPlayer PlayerUnit => PlayerUnits.TryGetValue(_currentProcessId, out var player) ? player : null;

        private static T[] GetUnits<T>(UnitType unitType, bool saveToCache = false) where T : UnitAny
        {
            var allUnits = new Dictionary<uint, T>();
            Func<IntPtr, T> CreateUnit = (ptr) => (T)Activator.CreateInstance(typeof(T), new object[] { ptr });

            var unitHashTable = GameManager.UnitHashTable(128 * 8 * (int)unitType);

            foreach (var ptrUnit in unitHashTable.UnitTable)
            {
                if (ptrUnit == IntPtr.Zero) continue;

                var unit = CreateUnit(ptrUnit);

                Action<object> UseCachedUnit = (seenUnit) =>
                {
                    var castedSeenUnit = (T)seenUnit;
                    castedSeenUnit.CopyFrom(unit);

                    allUnits[castedSeenUnit.UnitId] = castedSeenUnit;
                };

                do
                {
                    if (saveToCache && cache.TryGetValue(unit.UnitId, out var seenUnit1) && seenUnit1 is T && !allUnits.ContainsKey(((T)seenUnit1).UnitId))
                    {
                        UseCachedUnit(seenUnit1);
                    }
                    //else if (saveToCache && cache.TryGetValue(unit.HashString, out var seenUnit2) && seenUnit2 is T && !allUnits.ContainsKey(((T)seenUnit2).UnitId))
                    //{
                    //    UseCachedUnit(seenUnit2);
                    //}
                    else if (unit.IsValidUnit && !allUnits.ContainsKey(unit.UnitId))
                    {
                        allUnits[unit.UnitId] = unit;

                        if (saveToCache)
                        {
                            cache[unit.UnitId] = unit;
                            //cache[unit.HashString] = unit;
                        }
                    }
                } while (unit.Struct.pListNext != IntPtr.Zero && (unit = CreateUnit(unit.Struct.pListNext)).IsValidUnit);
            }

            return allUnits.Values.ToArray();
        }

        private static void UpdateMemoryData()
        {
            if (!Items.ItemUnitHashesSeen.ContainsKey(_currentProcessId))
            {
                Items.ItemUnitHashesSeen.Add(_currentProcessId, new HashSet<string>());
                Items.ItemUnitIdsSeen.Add(_currentProcessId, new HashSet<uint>());
                Items.ItemUnitIdsToSkip.Add(_currentProcessId, new HashSet<uint>());
                Items.InventoryItemUnitIdsToSkip.Add(_currentProcessId, new HashSet<uint>());
                Items.ItemVendors.Add(_currentProcessId, new Dictionary<uint, Npc>());
                Items.ItemLog.Add(_currentProcessId, new List<ItemLogEntry>());
            }
            else
            {
                Items.ItemUnitHashesSeen[_currentProcessId].Clear();
                Items.ItemUnitIdsSeen[_currentProcessId].Clear();
                Items.ItemUnitIdsToSkip[_currentProcessId].Clear();
                Items.InventoryItemUnitIdsToSkip[_currentProcessId].Clear();
                Items.ItemVendors[_currentProcessId].Clear();
                Items.ItemLog[_currentProcessId].Clear();
            }

            if (!Corpses.ContainsKey(_currentProcessId))
            {
                Corpses.Add(_currentProcessId, new Dictionary<string, UnitPlayer>());
            }
            else
            {
                Corpses[_currentProcessId].Clear();
            }
        }
    }
}
