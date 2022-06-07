﻿/**
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

using GameOverlay.Drawing;
using GameOverlay.Windows;
using MapAssist.Helpers;
using MapAssist.Settings;
using MapAssist.Types;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

//using WK.Libraries.HotkeyListenerNS;
using Graphics = GameOverlay.Drawing.Graphics;

namespace MapAssist
{
    public class Overlay : IDisposable
    {
        private static readonly NLog.Logger _log = NLog.LogManager.GetCurrentClassLogger();

        private static readonly HashSet<string> _townNames = new HashSet<string>()
        {
            "RogueEncampment",
            "LutGholein",
            "KurastDocks",
            "ThePandemoniumFortress",
            "Harrogath",
        };

        private readonly GraphicsWindow _window;
        private GameDataReader _gameDataReader;
        private GameData _gameData;
        private Compositor _compositor = new Compositor();
        private bool _show = true;
        private static readonly object _lock = new object();
        private static int _lastKnownHp;
        private static int _chickenHpPercent = 40;

        public Overlay()
        {
            _gameDataReader = new GameDataReader();

            GameOverlay.TimerService.EnableHighPrecisionTimers();

            var gfx = new Graphics() { MeasureFPS = true };
            gfx.PerPrimitiveAntiAliasing = true;
            gfx.TextAntiAliasing = true;

            _window = new GraphicsWindow(0, 0, 1, 1, gfx) { FPS = 60, IsVisible = true };

            _window.DrawGraphics += _window_DrawGraphics;
            _window.DestroyGraphics += _window_DestroyGraphics;

            var cfgChicken = MapAssistConfiguration.Loaded.ChickenHpPercent;
            if (cfgChicken > 0 && cfgChicken < 100)
            {
                _chickenHpPercent = cfgChicken;
                _log.Info("Loaded chicken life percent: " + _chickenHpPercent);
            }
            else
            {
                _log.Error("Invalid chicken life percent given: " + _chickenHpPercent + ", using default of 35 instead");
            }
        }

        private static void KillD2r()
        {
            var id = Process.GetCurrentProcess().SessionId;
            var procs = Process.GetProcessesByName("D2R");
            foreach (var proc in procs)
            {
                if (proc.SessionId == id)
                {
                    try { proc.Kill(); } catch (Exception) { }
                }
            }
        }

        private void _window_DrawGraphics(object sender, DrawGraphicsEventArgs e)
        {
            if (disposed) return;

            //var gfx = e.Graphics;

            try
            {
                lock (_lock)
                {
                    var (gameData, areaData, pointsOfInterest, changed) = _gameDataReader.Get();
                    _gameData = gameData;


                    if (_gameData != null && _gameData.PlayerUnit != null)
                    {
                        var playerUnit = _gameData.PlayerUnit;
                        var hp = 0;
                        var maxHp = 0;
                        if (playerUnit.Stats.TryGetValue(Stats.Stat.Life, out hp)) hp = hp >> 8;
                        if (playerUnit.Stats.TryGetValue(Stats.Stat.Life, out maxHp)) maxHp = maxHp >> 8;
                        if (hp != _lastKnownHp)
                        {
                            Console.WriteLine("Player life changed from " + _lastKnownHp + " to " + hp);
                        }
                        _lastKnownHp = hp;

                        if (!_townNames.Contains(_gameData.Area.ToString()) && playerUnit.LifePercentage > 0 && playerUnit.LifePercentage < _chickenHpPercent)
                        {
                            KillD2r();
                            _log.Info("D2R was killed by MAChicken due to life threshold being reached. Player life: " + playerUnit.LifePercentage + "%, threshold: " + _chickenHpPercent + "%");
                        }
                    }

                    //if (changed)
                    //{
                    //    _compositor.SetArea(areaData, pointsOfInterest);
                    //}

                    //gfx.ClearScene();

                    //if (_compositor != null && InGame() && _compositor != null && _gameData != null)
                    //{
                    //    UpdateLocation();

                    //    if (gfx.Width > 0 && gfx.Height > 0)
                    //    {
                    //        var errorLoadingAreaData = _compositor._areaData == null;

                    //        var overlayHidden = false;
                    //        //var overlayHidden = !_show ||
                    //        //    errorLoadingAreaData ||
                    //        //    (MapAssistConfiguration.Loaded.RenderingConfiguration.ToggleViaInGameMap && !_gameData.MenuOpen.Map) ||
                    //        //    (MapAssistConfiguration.Loaded.RenderingConfiguration.ToggleViaInGamePanels && _gameData.MenuPanelOpen > 0) ||
                    //        //    (MapAssistConfiguration.Loaded.RenderingConfiguration.ToggleViaInGamePanels && _gameData.MenuOpen.EscMenu) ||
                    //        //    Array.Exists(MapAssistConfiguration.Loaded.HiddenAreas, area => area == _gameData.Area) ||
                    //        //    _gameData.Area == Area.None ||
                    //        //    gfx.Width == 1 ||
                    //        //    gfx.Height == 1;

                    //        var size = MapAssistConfiguration.Loaded.RenderingConfiguration.Size;

                    //        var drawBounds = new Rectangle(0, 0, gfx.Width, gfx.Height * 0.78f);
                    //        switch (MapAssistConfiguration.Loaded.RenderingConfiguration.Position)
                    //        {
                    //            case MapPosition.TopLeft:
                    //                drawBounds = new Rectangle(PlayerIconWidth() + 40, PlayerIconWidth() + 100, 0, PlayerIconWidth() + 100 + size);
                    //                break;

                    //            case MapPosition.TopRight:
                    //                drawBounds = new Rectangle(0, 100, gfx.Width, 100 + size);
                    //                break;
                    //        }

                    //        _compositor.Init(gfx, _gameData, drawBounds);

                    //        if (!overlayHidden)
                    //        {
                    //            //_compositor.DrawGamemap(gfx);
                    //            //_compositor.DrawOverlay(gfx);
                    //            //_compositor.DrawBuffs(gfx);
                    //            //_compositor.DrawMonsterBar(gfx);
                    //        }

                    //        _compositor.DrawPlayerInfo(gfx);

                    //        var gameInfoAnchor = GameInfoAnchor(MapAssistConfiguration.Loaded.GameInfo.Position);
                    //        var nextAnchor = _compositor.DrawGameInfo(gfx, gameInfoAnchor, e, errorLoadingAreaData);

                    //        var itemLogAnchor = (MapAssistConfiguration.Loaded.ItemLog.Position == MapAssistConfiguration.Loaded.GameInfo.Position)
                    //            ? nextAnchor.Add(0, GameInfoPadding())
                    //            : GameInfoAnchor(MapAssistConfiguration.Loaded.ItemLog.Position);
                    //        _compositor.DrawItemLog(gfx, itemLogAnchor);
                    //    }
                    //}
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex);
            }
}

        public void Run()
        {
            _window.Create();
            _window.Join();
        }

        private bool InGame()
        {
            return _gameData != null && _gameData.MainWindowHandle != IntPtr.Zero;
        }

        public void KeyDownHandler(object sender, KeyEventArgs args)
        {
            return;
            //if (InGame() && GameManager.IsGameInForeground)
            //{
            //    var keys = new Hotkey(args.Modifiers, args.KeyCode);

            //    if (keys == new Hotkey(MapAssistConfiguration.Loaded.HotkeyConfiguration.ToggleKey))
            //    {
            //        _show = !_show;
            //    }

            //    if (keys == new Hotkey(MapAssistConfiguration.Loaded.HotkeyConfiguration.HideMapKey))
            //    {
            //        _show = false;
            //    }

            //    if (keys == new Hotkey(MapAssistConfiguration.Loaded.HotkeyConfiguration.AreaLevelKey))
            //    {
            //        MapAssistConfiguration.Loaded.GameInfo.ShowAreaLevel = !MapAssistConfiguration.Loaded.GameInfo.ShowAreaLevel;
            //    }

            //    if (keys == new Hotkey(MapAssistConfiguration.Loaded.HotkeyConfiguration.ZoomInKey))
            //    {
            //        var zoomLevel = MapAssistConfiguration.Loaded.RenderingConfiguration.ZoomLevel;

            //        if (MapAssistConfiguration.Loaded.RenderingConfiguration.ZoomLevel > 0.1f)
            //        {
            //            MapAssistConfiguration.Loaded.RenderingConfiguration.ZoomLevel -= zoomLevel <= 1 ? 0.1 : 0.2;
            //            MapAssistConfiguration.Loaded.RenderingConfiguration.Size +=
            //              (int)(MapAssistConfiguration.Loaded.RenderingConfiguration.InitialSize * 0.05f);
            //        }
            //    }

            //    if (keys == new Hotkey(MapAssistConfiguration.Loaded.HotkeyConfiguration.ZoomOutKey))
            //    {
            //        var zoomLevel = MapAssistConfiguration.Loaded.RenderingConfiguration.ZoomLevel;

            //        if (MapAssistConfiguration.Loaded.RenderingConfiguration.ZoomLevel < 4f)
            //        {
            //            MapAssistConfiguration.Loaded.RenderingConfiguration.ZoomLevel += zoomLevel >= 1 ? 0.2 : 0.1;
            //            MapAssistConfiguration.Loaded.RenderingConfiguration.Size -=
            //              (int)(MapAssistConfiguration.Loaded.RenderingConfiguration.InitialSize * 0.05f);
            //        }
            //    }

            //    if (keys == new Hotkey(MapAssistConfiguration.Loaded.HotkeyConfiguration.ExportItemsKey))
            //    {
            //        if (InGame())
            //        {
            //            ItemExport.ExportPlayerInventory(_gameData.PlayerUnit, _gameData.AllItems);
            //        }
            //    }
            //}
        }

        /// <summary>
        /// Resize overlay to currently active screen
        /// </summary>
        private void UpdateLocation()
        {
            var rect = WindowRect();
            var ultraWideMargin = UltraWideMargin();

            _window.Resize((int)(rect.Left + ultraWideMargin), (int)rect.Top, (int)(rect.Right - rect.Left - ultraWideMargin * 2), (int)(rect.Bottom - rect.Top));
            _window.PlaceAbove(_gameData.MainWindowHandle);
        }

        private Rectangle WindowRect()
        {
            WindowBounds rect;
            WindowHelper.GetWindowClientBounds(_gameData.MainWindowHandle, out rect);

            return new Rectangle(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        private float UltraWideMargin()
        {
            var rect = WindowRect();
            return (float)Math.Max(Math.Round(((rect.Width + 2) - (rect.Height + 4) * 2.1f) / 2f), 0);
        }

        private float PlayerIconWidth()
        {
            var rect = WindowRect();
            return rect.Height / 20f;
        }

        private float GameInfoPadding()
        {
            var rect = WindowRect();
            return rect.Height / 100f;
        }

        private Point GameInfoAnchor(GameInfoPosition position)
        {
            switch (position)
            {
                case GameInfoPosition.TopLeft:
                    var margin = _window.Height / 18f;
                    return new Point(PlayerIconWidth() + margin, PlayerIconWidth() + margin);

                case GameInfoPosition.TopRight:
                    var rightMargin = _window.Width / 60f;
                    var topMargin = _window.Height / 35f;
                    return new Point(_window.Width - rightMargin, topMargin);
            }
            return new Point();
        }

        private void _window_DestroyGraphics(object sender, DestroyGraphicsEventArgs e)
        {
            if (_compositor != null) _compositor.Dispose();
            _compositor = null;
        }

        ~Overlay() => Dispose();

        private bool disposed = false;

        public void Dispose()
        {
            lock (_lock)
            {
                if (!disposed)
                {
                    disposed = true; // This first to let GraphicsWindow.DrawGraphics know to return instantly
                    _window.Dispose(); // This second to dispose of GraphicsWindow
                    if (_compositor != null) _compositor.Dispose(); // This last so it's disposed after GraphicsWindow stops using it
                }
            }
        }
    }
}
