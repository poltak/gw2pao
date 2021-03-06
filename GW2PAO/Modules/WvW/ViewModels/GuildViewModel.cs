﻿using GW2PAO.API.Data;
using GW2PAO.PresentationCore;
using Microsoft.Practices.Prism.Mvvm;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GW2PAO.Modules.WvW.ViewModels
{
    public class GuildViewModel : BindableBase
    {
        /// <summary>
        /// Default logger
        /// </summary>
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private Guid? guildId;
        private string guildName;
        private string guildTag;

        /// <summary>
        /// The guild's ID
        /// </summary>
        public Guid? ID
        {
            get { return this.guildId; }
            set { this.SetProperty(ref this.guildId, value); }
        }

        /// <summary>
        /// The guild's Name
        /// </summary>
        public string Name
        {
            get { return this.guildName; }
            set { this.SetProperty(ref this.guildName, value); }
        }

        /// <summary>
        /// the guild's Tag
        /// </summary>
        public string Tag
        {
            get { return this.guildTag; }
            set { this.SetProperty(ref this.guildTag, value); }
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="modelData">The guild data</param>
        public GuildViewModel()
        {
            this.ID = null;
            this.Name = string.Empty;
            this.Tag = string.Empty;
        }
    }
}
