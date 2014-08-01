﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GW2PAO.API.Data;
using GW2PAO.API.Services;
using GW2PAO.Models;
using GW2PAO.PresentationCore;
using NLog;

namespace GW2PAO.ViewModels.DungeonTracker
{
    public class DungeonViewModel : NotifyPropertyChangedBase
    {
        /// <summary>
        /// Default logger
        /// </summary>
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private DungeonSettings userSettings;
        private bool isVisible;

        /// <summary>
        /// The primary model object containing the dungeon information
        /// </summary>
        public Dungeon DungeonModel { get; private set; }

        /// <summary>
        /// The dungeons's ID
        /// </summary>
        public Guid DungeonId { get { return this.DungeonModel.ID; } }

        /// <summary>
        /// The dungeons's name
        /// </summary>
        public string DungeonName { get { return this.DungeonModel.Name; } }

        /// <summary>
        /// Name of the zone in which the dungeon is located
        /// </summary>
        public string ZoneName { get { return "Located in " + this.DungeonModel.Location; } }

        /// <summary>
        /// Minimum level requirement for the dungeon
        /// </summary>
        public string MinimumLevel { get { return "Minimum Level: " + this.DungeonModel.MinimumLevel; } }

        /// <summary>
        /// Command to hide the dungeon
        /// </summary>
        public DelegateCommand HideCommand { get { return new DelegateCommand(this.AddToHiddenDungeons); } }

        /// <summary>
        /// Command to open the wiki page for this dungeon
        /// </summary>
        public DelegateCommand OpenWikiPageCommand { get { return new DelegateCommand(this.OpenWikiPage); } }

        /// <summary>
        /// Visibility of the dungeon
        /// Visibility is based on multiple properties, including:
        ///     - Minimum Level and Character's Level, and the user configuration for if unreachable dungeons are shown
        ///     - Whether or not the dungeon is user-configured as hidden
        /// </summary>
        public bool IsVisible
        {
            get { return this.isVisible; }
            set { SetField(ref this.isVisible, value); }
        }

        /// <summary>
        /// Collection of dungeon paths for this dungeon
        /// </summary>
        public ObservableCollection<PathViewModel> Paths { get; private set; }

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="dungeon">The dungeon information</param>
        /// <param name="userSettings">The dungeon user settings</param>
        public DungeonViewModel(Dungeon dungeon, DungeonSettings userSettings)
        {
            this.DungeonModel = dungeon;
            this.userSettings = userSettings;

            // Initialize the path view models
            this.Paths = new ObservableCollection<PathViewModel>();
            foreach (var path in this.DungeonModel.Paths)
            {
                this.Paths.Add(new PathViewModel(path, this.userSettings));
            }

            this.RefreshVisibility();
            this.userSettings.PropertyChanged += (o, e) => this.RefreshVisibility();
            this.userSettings.HiddenDungeons.CollectionChanged += (o, e) => this.RefreshVisibility();
        }

        /// <summary>
        /// Adds the dungeon to the list of hidden dungeons in the user settings
        /// </summary>
        private void AddToHiddenDungeons()
        {
            logger.Debug("Adding \"{0}\" to hidden dungeons", this.DungeonName);
            this.userSettings.HiddenDungeons.Add(this.DungeonId);
        }

        /// <summary>
        /// Opens the wiki page for this dungeon
        /// </summary>
        private void OpenWikiPage()
        {
            Process.Start(this.DungeonModel.WikiUrl);
        }

        /// <summary>
        /// Refreshes the visibility of the event
        /// </summary>
        private void RefreshVisibility()
        {
            logger.Trace("Refreshing visibility of \"{0}\"", this.DungeonName);
            if (this.userSettings.HiddenDungeons.Any(id => id == this.DungeonId))
            {
                this.IsVisible = false;
            }
            else
            {
                this.IsVisible = true;
            }
            logger.Trace("IsVisible = {0}", this.IsVisible);
        }
    }
}