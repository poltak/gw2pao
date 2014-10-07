﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GW2PAO.API.Services.Interfaces;
using GW2PAO.API.Util;
using GW2PAO.Controllers.Interfaces;
using GW2PAO.Models;
using GW2PAO.Utility;
using GW2PAO.ViewModels.Interfaces;
using GW2PAO.ViewModels.ZoneCompletion;
using NLog;

namespace GW2PAO.Controllers
{
    /// <summary>
    /// The Zone Completion Assistant controller. Handles refresh of current zone and zone point locations
    /// </summary>
    public class ZoneCompletionController : IZoneCompletionController
    {
        /// <summary>
        /// Default logger
        /// </summary>
        private static Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Service responsible for Zone information
        /// </summary>
        private IZoneService zoneService;

        /// <summary>
        /// Service responsible for Player information
        /// </summary>
        private IPlayerService playerService;

        /// <summary>
        /// Service responsible for System information
        /// </summary>
        private ISystemService systemService;

        /// <summary>
        /// ViewModel object holding the current zone name
        /// </summary>
        private IHasZoneName zoneNameObject;

        /// <summary>
        /// Timer for refreshing the current zone
        /// </summary>
        private Timer zoneRefreshTimer;

        /// <summary>
        /// Locking object for operations performed with the zone refresh timer
        /// </summary>
        private readonly object zoneRefreshTimerLock = new object();

        /// <summary>
        /// Timer for refreshing zone item locations
        /// </summary>
        private Timer itemLocationsRefreshTimer;

        /// <summary>
        /// Locking object for operations performed with the item locations refresh timer
        /// </summary>
        private readonly object locationsRefreshTimerLock = new object();

        /// <summary>
        /// Keeps track of how many times Start() has been called in order
        /// to support reuse of a single object
        /// </summary>
        private int startCallCount;

        /// <summary>
        /// Dictionary of counters for auto-unlock purposes
        /// </summary>
        private Dictionary<int, int> distanceCounters = new Dictionary<int, int>();

        /// <summary>
        /// Backing store for the zone items collection
        /// </summary>
        private ObservableCollection<ZoneItemViewModel> zoneItems = new ObservableCollection<ZoneItemViewModel>();

        /// <summary>
        /// The collection of zone points in the current zone
        /// </summary>
        public ObservableCollection<ZoneItemViewModel> ZoneItems { get { return this.zoneItems; } }

        /// <summary>
        /// Locking object for operations performed with the zone items collection
        /// </summary>
        private readonly object zoneItemsLock = new object();

        /// <summary>
        /// The current character's name
        /// </summary>
        public string CharacterName { get; private set; }

        /// <summary>
        /// The zone completion user settings
        /// </summary>
        public ZoneCompletionSettings UserSettings { get; private set; }

        /// <summary>
        /// The interval by which to refresh zone information (in ms)
        /// </summary>
        public int ZoneRefreshInterval { get; set; }

        /// <summary>
        /// The interval by which to refresh zone point locations (in ms)
        /// </summary>
        public int LocationsRefreshInterval { get; set; }

        /// <summary>
        /// The ID of the current map/zone
        /// </summary>
        public int CurrentMapID { get; private set; }

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="zoneService">The zone service</param>
        /// <param name="playerService">The player service</param>
        /// <param name="systemService">The system service</param>
        /// <param name="zoneNameObject">Zone name viewmodel object</param>
        /// <param name="userSettings">User settings</param>
        public ZoneCompletionController(IZoneService zoneService, IPlayerService playerService, ISystemService systemService, IHasZoneName zoneNameObject, ZoneCompletionSettings userSettings)
        {
            logger.Debug("Initializing Zone Completion Controller");
            this.zoneService = zoneService;
            this.playerService = playerService;
            this.systemService = systemService;
            this.zoneNameObject = zoneNameObject;

            this.UserSettings = userSettings;

            // Initialize refresh timers
            this.zoneRefreshTimer = new Timer(this.RefreshZone);
            this.ZoneRefreshInterval = 1000;
            this.itemLocationsRefreshTimer = new Timer(this.RefreshLocations);
            this.LocationsRefreshInterval = 250; // TODO: Tweak this until we get good performance without sucking up the CPU

            this.startCallCount = 0;
            this.CurrentMapID = -1;
            logger.Info("Zone Completion Controller initialized");
        }

        /// <summary>
        /// Starts the automatic refresh of the ZoneCompletionController
        /// </summary>
        public void Start()
        {
            logger.Debug("Start called");
            Task.Factory.StartNew(() =>
            {
                // Start the timer if this is the first time that Start() has been called
                if (this.startCallCount == 0)
                {
                    logger.Debug("Starting refresh timers");
                    this.RefreshZone();
                    this.RefreshLocations(null);
                }

                this.startCallCount++;
                logger.Debug("startCallCount = " + this.startCallCount);

            }, TaskCreationOptions.LongRunning);
        }

        /// <summary>
        /// Stops the automatic refresh of the ZoneCompletionController
        /// </summary>
        public void Stop()
        {
            this.startCallCount--;
            logger.Debug("Stop called - startCallCount = " + this.startCallCount);

            // Stop the refresh timer if all calls to Start() have had a matching call to Stop()
            if (this.startCallCount == 0)
            {
                logger.Debug("Stopping refresh timers");
                lock (zoneRefreshTimerLock)
                {
                    this.zoneRefreshTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
                lock (locationsRefreshTimerLock)
                {
                    this.itemLocationsRefreshTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }

        /// <summary>
        /// Primary method for refreshing the current zone
        /// </summary>
        /// <param name="state"></param>
        private void RefreshZone(object state = null)
        {
            lock (zoneRefreshTimerLock)
            {
                if (this.systemService.IsGw2Running && this.playerService.HasValidMapId)
                {
                    // Check to see if the MapId or Character Name has changed, if so, we need to clear our zone items and add the new ones
                    if (this.CurrentMapID != this.playerService.MapId
                        || this.CharacterName != this.playerService.CharacterName)
                    {
                        logger.Info("Map/Character change detected, resetting zone events. New MapID = {0} | Character Name = {1}", this.playerService.MapId, this.CharacterName);
                        this.CurrentMapID = this.playerService.MapId;
                        this.CharacterName = this.playerService.CharacterName;

                        var zoneItems = this.zoneService.GetZoneItems(this.playerService.MapId);
                        Threading.InvokeOnUI(() =>
                        {
                            lock (zoneItemsLock)
                            {
                                this.ZoneItems.Clear();
                                this.distanceCounters.Clear();
                                foreach (var item in zoneItems)
                                {
                                    // Ignore dungeons for now
                                    if (item.Type != API.Data.Enums.ZoneItemType.Dungeon)
                                    {
                                        this.ZoneItems.Add(new ZoneItemViewModel(item, this.playerService, this.UserSettings));
                                        this.distanceCounters.Add(item.ID, 0);
                                    }
                                }
                            }
                        });

                        // Update the current zone name
                        var newZoneName = this.zoneService.GetZoneName(this.CurrentMapID);
                        if (this.zoneNameObject.ZoneName != newZoneName)
                        {
                            Threading.InvokeOnUI(() => this.zoneNameObject.ZoneName = newZoneName);
                        }
                        logger.Info("New Zone Name = {0}", newZoneName);
                    }
                }

                this.zoneRefreshTimer.Change(this.ZoneRefreshInterval, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Primary method for refreshing the locations of points in the current zone
        /// </summary>
        /// <param name="state"></param>
        private void RefreshLocations(object state = null)
        {
            lock (locationsRefreshTimerLock)
            {
                var playerPosition = CalcUtil.ConvertToMapPosition(this.playerService.PlayerPosition);
                var cameraDirectionPosition = CalcUtil.ConvertToMapPosition(this.playerService.CameraDirection);

                lock (this.zoneItemsLock)
                {
                    foreach (var item in this.ZoneItems)
                    {
                        var newDistance = Math.Round(CalcUtil.CalculateDistance(playerPosition, item.ItemModel.Location, this.UserSettings.DistanceUnits));
                        var newAngle = CalcUtil.CalculateAngle(CalcUtil.Vector.CreateVector(playerPosition, item.ItemModel.Location),
                                                               CalcUtil.Vector.CreateVector(new API.Data.Point(0, 0), cameraDirectionPosition));

                        if (item.DistanceFromPlayer != newDistance)
                        {
                            Threading.BeginInvokeOnUI(() => item.DistanceFromPlayer = newDistance);
                        }

                        if (item.DirectionFromPlayer != newAngle)
                        {
                            Threading.BeginInvokeOnUI(() => item.DirectionFromPlayer = newAngle);
                        }

                        if (!item.IsUnlocked)
                        {
                            // If the zone item isn't already unlocked, check to see if it should be automatically unlocked
                            //  based on the item's distance from the player and based on how long the player has been near the item
                            var ftDistance = Math.Round(CalcUtil.CalculateDistance(playerPosition, item.ItemModel.Location, API.Data.Enums.Units.Feet));
                            switch (item.ItemType)
                            {
                                case API.Data.Enums.ZoneItemType.Waypoint:
                                    if (this.UserSettings.AutoUnlockWaypoints
                                        && ftDistance >= 0 && ftDistance < 75)
                                    {
                                        Threading.BeginInvokeOnUI(() => item.IsUnlocked = true);
                                    }
                                    break;
                                case API.Data.Enums.ZoneItemType.PointOfInterest:
                                    if (this.UserSettings.AutoUnlockPois
                                        && ftDistance >= 0 && ftDistance < 75)
                                    {
                                        Threading.BeginInvokeOnUI(() => item.IsUnlocked = true);
                                    }
                                    break;
                                case API.Data.Enums.ZoneItemType.Vista:
                                    if (this.UserSettings.AutoUnlockVistas
                                        && ftDistance >= 0 && ftDistance < 8)
                                    {
                                        if (this.distanceCounters[item.ItemId] > 4)
                                        {
                                            this.distanceCounters[item.ItemId] = 0;
                                            Threading.BeginInvokeOnUI(() => item.IsUnlocked = true);
                                        }
                                        else
                                        {
                                            this.distanceCounters[item.ItemId] += 1;
                                        }
                                    }
                                    else
                                    {
                                        this.distanceCounters[item.ItemId] = 0;
                                    }
                                    break;
                                case API.Data.Enums.ZoneItemType.HeartQuest:
                                    if (this.UserSettings.AutoUnlockHeartQuests
                                        && ftDistance >= 0 && ftDistance < 400)
                                    {
                                        if (this.distanceCounters[item.ItemId] > 90)
                                        {
                                            this.distanceCounters[item.ItemId] = 0;
                                            Threading.BeginInvokeOnUI(() => item.IsUnlocked = true);
                                        }
                                        else
                                        {
                                            this.distanceCounters[item.ItemId] += 1;
                                        }
                                    }
                                    else
                                    {
                                        this.distanceCounters[item.ItemId] = 0;
                                    }
                                    break;
                                case API.Data.Enums.ZoneItemType.SkillChallenge:
                                    if (this.UserSettings.AutoUnlockSkillChallenges
                                        && ftDistance >= 0 && ftDistance < 25)
                                    {
                                        if (this.distanceCounters[item.ItemId] > 15)
                                        {
                                            this.distanceCounters[item.ItemId] = 0;
                                            Threading.BeginInvokeOnUI(() => item.IsUnlocked = true);
                                        }
                                        else
                                        {
                                            this.distanceCounters[item.ItemId] += 1;
                                        }
                                    }
                                    else
                                    {
                                        this.distanceCounters[item.ItemId] = 0;
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                }

                this.itemLocationsRefreshTimer.Change(this.LocationsRefreshInterval, Timeout.Infinite);
            }
        }
    }
}
