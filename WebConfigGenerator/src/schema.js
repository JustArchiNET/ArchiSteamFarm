import Validators from './validators';

export default {
    'V3.2.0.3+': {
        asf: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        label: 'SteamOwnerID',
                        field: 's_SteamOwnerID',
                        placeholder: '0',
                        type: 'InputText',
                        description: 'schema.generic.steamid64',
                        validator: Validators.steamid
                    }
                ]
            },
            {
                legend: 'schema.trading',
                advanced: true,
                fields: [
                    {
                        label: 'MaxTradeHoldDuration',
                        field: 'MaxTradeHoldDuration',
                        placeholder: 15,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.customization',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'AutoRestart',
                        label: 'AutoRestart',
                        defaultValue: true
                    },
                    {
                        label: 'Blacklist',
                        field: 'Blacklist',
                        type: 'InputSet',
                        validator: Validators.uint
                    },
                    {
                        field: 'CommandPrefix',
                        label: 'CommandPrefix',
                        type: 'InputText',
                        placeholder: '!'
                    },
                    {
                        label: 'CurrentCulture',
                        field: 'CurrentCulture',
                        type: 'InputText',
                        placeholder: 'en-US'
                    },
                    {
                        type: 'InputCheckbox',
                        field: 'Statistics',
                        label: 'Statistics',
                        defaultValue: true
                    }
                ]
            },
            {
                legend: 'schema.remote_access',
                advanced: true,
                fields: [
                    {
                        label: 'Headless',
                        field: 'Headless',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'IPC',
                        field: 'IPC',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'IPCPassword',
                        field: 'IPCPassword',
                        placeholder: '',
                        type: 'InputPassword'
                    },
                    {
                        label: 'IPCPrefixes',
                        field: 'IPCPrefixes',
                        type: 'InputSet'
                    }
                ]
            },
            {
                legend: 'schema.connection',
                advanced: true,
                fields: [
                    {
                        label: 'ConnectionTimeout',
                        field: 'ConnectionTimeout',
                        placeholder: 60,
                        type: 'InputNumber',
                        validator: Validators.byte
                    },
                    {
                        type: 'InputFlag',
                        label: 'SteamProtocols',
                        field: 'SteamProtocols',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'TCP' },
                            { value: 2, name: 'UDP' },
                            { value: 4, name: 'WebSocket' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        label: 'WebProxy',
                        field: 'WebProxy',
                        placeholder: '',
                        type: 'InputText'
                    },
                    {
                        label: 'WebProxyPassword',
                        field: 'WebProxyPassword',
                        placeholder: '',
                        type: 'InputPassword'
                    },
                    {
                        label: 'WebProxyUsername',
                        field: 'WebProxyUsername',
                        placeholder: '',
                        type: 'InputText'
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        label: 'ConfirmationsLimiterDelay',
                        field: 'ConfirmationsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'FarmingDelay',
                        field: 'FarmingDelay',
                        type: 'InputNumber',
                        placeholder: 15,
                        validator: Validators.byte
                    },
                    {
                        label: 'GiftsLimiterDelay',
                        field: 'GiftsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 1,
                        validator: Validators.byte
                    },
                    {
                        label: 'IdleFarmingPeriod',
                        field: 'IdleFarmingPeriod',
                        type: 'InputNumber',
                        placeholder: 8,
                        validator: Validators.byte
                    },
                    {
                        label: 'InventoryLimiterDelay',
                        field: 'InventoryLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'LoginLimiterDelay',
                        field: 'LoginLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'MaxFarmingTime',
                        field: 'MaxFarmingTime',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'OptimizationMode',
                        field: 'OptimizationMode',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'MaxPerformance' },
                            { value: 1, name: 'MinMemoryUsage' }
                        ],
                        defaultValue: 0
                    },
                    {
                        label: 'WebLimiterDelay',
                        field: 'WebLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 200,
                        validator: Validators.ushort
                    }
                ]
            },
            {
                legend: 'schema.updates',
                advanced: true,
                fields: [
                    {
                        label: 'UpdateChannel',
                        field: 'UpdateChannel',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Stable' },
                            { value: 2, name: 'Experimental' }
                        ],
                        defaultValue: 1
                    },
                    {
                        label: 'UpdatePeriod',
                        field: 'UpdatePeriod',
                        type: 'InputNumber',
                        placeholder: 24,
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.advanced',
                advanced: true,
                fields: [
                    {
                        label: 'Debug',
                        field: 'Debug',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    }
                ]
            }
        ],
        bot: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        type: 'InputText',
                        label: 'Name',
                        field: 'name',
                        required: true,
                        description: 'schema.bot.name.description'
                    },
                    {
                        type: 'InputText',
                        label: 'SteamLogin',
                        field: 'SteamLogin',
                        description: 'schema.bot.SteamLogin.description'
                    },
                    {
                        type: 'InputPassword',
                        label: 'SteamPassword',
                        field: 'SteamPassword',
                        description: 'schema.bot.SteamPassword.description'
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Enabled',
                        field: 'Enabled',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Paused',
                        field: 'Paused',
                        defaultValue: false,
                        advanced: true
                    },
                    {
                        type: 'InputText',
                        label: 'SteamParentalPIN',
                        field: 'SteamParentalPIN',
                        placeholder: 0,
                        validator: Validators.parentalPIN,
                        advanced: true
                    },
                    {
                        type: 'InputFlag',
                        label: 'BotBehaviour',
                        field: 'BotBehaviour',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'RejectInvalidFriendInvites' },
                            { value: 2, name: 'RejectInvalidTrades' },
                            { value: 4, name: 'RejectInvalidGroupInvites' },
                            { value: 8, name: 'DismissInventoryNotifications' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.access',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamMasterClanID',
                        field: 's_SteamMasterClanID',
                        placeholder: 0,
                        validator: Validators.masterClan
                    },
                    {
                        type: 'InputMap',
                        label: 'SteamUserPermissions',
                        field: 'SteamUserPermissions',
                        keyPlaceholder: 'SteamID64',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'FamilySharing' },
                            { value: 2, name: 'Operator' },
                            { value: 3, name: 'Master' }
                        ],
                        defaultValue: 0,
                        keyValidator: Validators.steamid
                    }
                ]
            },
            {
                legend: 'schema.trading',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'AcceptGifts',
                        field: 'AcceptGifts',
                        defaultValue: false
                    },
                    {
                        type: 'InputText',
                        label: 'SteamTradeToken',
                        field: 'SteamTradeToken',
                        validator: Validators.tradeToken
                    },
                    {
                        type: 'InputFlag',
                        label: 'TradingPreferences',
                        field: 'TradingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'AcceptDonations' },
                            { value: 2, name: 'SteamTradeMatcher' },
                            { value: 4, name: 'MatchEverything' },
                            { value: 8, name: 'DontAcceptBotTrades' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'LootableTypes',
                        field: 'LootableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ]
                    },
                    {
                        type: 'InputSet',
                        label: 'MatchableTypes',
                        field: 'MatchableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ]
                    }
                ]
            },
            {
                legend: 'schema.farming',
                advanced: true,
                fields: [
                    {
                        label: 'HoursUntilCardDrops',
                        field: 'HoursUntilCardDrops',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputSet',
                        label: 'FarmingOrders',
                        field: 'FarmingOrders',
                        values: [
                            { value: 0, name: 'Unordered' },
                            { value: 1, name: 'AppIDsAscending' },
                            { value: 2, name: 'AppIDsDescending' },
                            { value: 3, name: 'CardDropsAscending' },
                            { value: 4, name: 'CardDropsDescending' },
                            { value: 5, name: 'HoursAscending' },
                            { value: 6, name: 'HoursDescending' },
                            { value: 7, name: 'NamesAscending' },
                            { value: 8, name: 'NamesDescending' },
                            { value: 9, name: 'Random' },
                            { value: 10, name: 'BadgeLevelsAscending' },
                            { value: 11, name: 'BadgeLevelsDescending' },
                            { value: 12, name: 'RedeemDateTimesAscending' },
                            { value: 13, name: 'RedeemDateTimesDescending' }
                        ]
                    },
                    {
                        type: 'InputNumber',
                        label: 'SendTradePeriod',
                        field: 'SendTradePeriod',
                        placeholder: 0,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AutoSteamSaleEvent',
                        field: 'AutoSteamSaleEvent',
                        defaultValue: false
                    },
                    {
                        type: 'InputSelect',
                        label: 'OnlineStatus',
                        field: 'OnlineStatus',
                        options: [
                            { value: 0, name: 'Offline' },
                            { value: 1, name: 'Online' },
                            { value: 2, name: 'Busy' },
                            { value: 3, name: 'Away' },
                            { value: 4, name: 'Snooze' },
                            { value: 5, name: 'LookingToTrade' },
                            { value: 6, name: 'LookingToPlay' },
                            { value: 7, name: 'Invisible' }
                        ],
                        defaultValue: 1
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'HandleOfflineMessages',
                        field: 'HandleOfflineMessages',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IdlePriorityQueueOnly',
                        field: 'IdlePriorityQueueOnly',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IdleRefundableGames',
                        field: 'IdleRefundableGames',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'SendOnFarmingFinished',
                        field: 'SendOnFarmingFinished',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'ShutdownOnFarmingFinished',
                        field: 'ShutdownOnFarmingFinished',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.customization',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'RedeemingPreferences',
                        field: 'RedeemingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Forwarding' },
                            { value: 2, name: 'Distributing' },
                            { value: 4, name: 'KeepMissingGames' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'GamesPlayedWhileIdle',
                        field: 'GamesPlayedWhileIdle',
                        validator: Validators.uint
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileFarming',
                        field: 'CustomGamePlayedWhileFarming'
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileIdle',
                        field: 'CustomGamePlayedWhileIdle'
                    }
                ]
            }
        ]
    },
    'V3.2.0.1-V3.2.0.2': {
        asf: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        label: 'SteamOwnerID',
                        field: 's_SteamOwnerID',
                        placeholder: '0',
                        type: 'InputText',
                        description: 'schema.generic.steamid64',
                        validator: Validators.steamid
                    }
                ]
            },
            {
                legend: 'schema.trading',
                advanced: true,
                fields: [
                    {
                        label: 'MaxTradeHoldDuration',
                        field: 'MaxTradeHoldDuration',
                        placeholder: 15,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.customization',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'AutoRestart',
                        label: 'AutoRestart',
                        defaultValue: true
                    },
                    {
                        label: 'Blacklist',
                        field: 'Blacklist',
                        type: 'InputSet',
                        validator: Validators.uint
                    },
                    {
                        field: 'CommandPrefix',
                        label: 'CommandPrefix',
                        type: 'InputText',
                        placeholder: '!'
                    },
                    {
                        label: 'CurrentCulture',
                        field: 'CurrentCulture',
                        type: 'InputText',
                        placeholder: 'en-US'
                    },
                    {
                        type: 'InputCheckbox',
                        field: 'Statistics',
                        label: 'Statistics',
                        defaultValue: true
                    }
                ]
            },
            {
                legend: 'schema.remote_access',
                advanced: true,
                fields: [
                    {
                        label: 'Headless',
                        field: 'Headless',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'IPC',
                        field: 'IPC',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'IPCPassword',
                        field: 'IPCPassword',
                        placeholder: '',
                        type: 'InputPassword'
                    },
                    {
                        label: 'IPCPrefixes',
                        field: 'IPCPrefixes',
                        type: 'InputSet'
                    }
                ]
            },
            {
                legend: 'schema.connection',
                advanced: true,
                fields: [
                    {
                        label: 'ConnectionTimeout',
                        field: 'ConnectionTimeout',
                        placeholder: 60,
                        type: 'InputNumber',
                        validator: Validators.byte
                    },
                    {
                        type: 'InputFlag',
                        label: 'SteamProtocols',
                        field: 'SteamProtocols',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'TCP' },
                            { value: 2, name: 'UDP' },
                            { value: 4, name: 'WebSocket' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        label: 'WebProxy',
                        field: 'WebProxy',
                        placeholder: '',
                        type: 'InputText'
                    },
                    {
                        label: 'WebProxyPassword',
                        field: 'WebProxyPassword',
                        placeholder: '',
                        type: 'InputPassword'
                    },
                    {
                        label: 'WebProxyUsername',
                        field: 'WebProxyUsername',
                        placeholder: '',
                        type: 'InputText'
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        label: 'ConfirmationsLimiterDelay',
                        field: 'ConfirmationsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'FarmingDelay',
                        field: 'FarmingDelay',
                        type: 'InputNumber',
                        placeholder: 15,
                        validator: Validators.byte
                    },
                    {
                        label: 'GiftsLimiterDelay',
                        field: 'GiftsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 1,
                        validator: Validators.byte
                    },
                    {
                        label: 'IdleFarmingPeriod',
                        field: 'IdleFarmingPeriod',
                        type: 'InputNumber',
                        placeholder: 8,
                        validator: Validators.byte
                    },
                    {
                        label: 'InventoryLimiterDelay',
                        field: 'InventoryLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'LoginLimiterDelay',
                        field: 'LoginLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'MaxFarmingTime',
                        field: 'MaxFarmingTime',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'OptimizationMode',
                        field: 'OptimizationMode',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'MaxPerformance' },
                            { value: 1, name: 'MinMemoryUsage' }
                        ],
                        defaultValue: 0
                    },
                    {
                        label: 'WebLimiterDelay',
                        field: 'WebLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 200,
                        validator: Validators.ushort
                    }
                ]
            },
            {
                legend: 'schema.updates',
                advanced: true,
                fields: [
                    {
                        label: 'UpdateChannel',
                        field: 'UpdateChannel',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Stable' },
                            { value: 2, name: 'Experimental' }
                        ],
                        defaultValue: 1
                    },
                    {
                        label: 'UpdatePeriod',
                        field: 'UpdatePeriod',
                        type: 'InputNumber',
                        placeholder: 24,
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.advanced',
                advanced: true,
                fields: [
                    {
                        label: 'Debug',
                        field: 'Debug',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    }
                ]
            }
        ],
        bot: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        type: 'InputText',
                        label: 'Name',
                        field: 'name',
                        required: true,
                        description: 'schema.bot.name.description'
                    },
                    {
                        type: 'InputText',
                        label: 'SteamLogin',
                        field: 'SteamLogin',
                        description: 'schema.bot.SteamLogin.description'
                    },
                    {
                        type: 'InputPassword',
                        label: 'SteamPassword',
                        field: 'SteamPassword',
                        description: 'schema.bot.SteamPassword.description'
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Enabled',
                        field: 'Enabled',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Paused',
                        field: 'Paused',
                        defaultValue: false,
                        advanced: true
                    },
                    {
                        type: 'InputText',
                        label: 'SteamParentalPIN',
                        field: 'SteamParentalPIN',
                        placeholder: 0,
                        validator: Validators.parentalPIN,
                        advanced: true
                    },
                    {
                        type: 'InputFlag',
                        label: 'BotBehaviour',
                        field: 'BotBehaviour',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'RejectInvalidFriendInvites' },
                            { value: 2, name: 'RejectInvalidTrades' },
                            { value: 4, name: 'RejectInvalidGroupInvites' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.access',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamMasterClanID',
                        field: 's_SteamMasterClanID',
                        placeholder: 0,
                        validator: Validators.masterClan
                    },
                    {
                        type: 'InputMap',
                        label: 'SteamUserPermissions',
                        field: 'SteamUserPermissions',
                        keyPlaceholder: 'SteamID64',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'FamilySharing' },
                            { value: 2, name: 'Operator' },
                            { value: 3, name: 'Master' }
                        ],
                        defaultValue: 0,
                        keyValidator: Validators.steamid
                    }
                ]
            },
            {
                legend: 'schema.trading',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'AcceptGifts',
                        field: 'AcceptGifts',
                        defaultValue: false
                    },
                    {
                        type: 'InputText',
                        label: 'SteamTradeToken',
                        field: 'SteamTradeToken',
                        validator: Validators.tradeToken
                    },
                    {
                        type: 'InputFlag',
                        label: 'TradingPreferences',
                        field: 'TradingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'AcceptDonations' },
                            { value: 2, name: 'SteamTradeMatcher' },
                            { value: 4, name: 'MatchEverything' },
                            { value: 8, name: 'DontAcceptBotTrades' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'LootableTypes',
                        field: 'LootableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'MatchableTypes',
                        field: 'MatchableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.farming',
                advanced: true,
                fields: [
                    {
                        label: 'HoursUntilCardDrops',
                        field: 'HoursUntilCardDrops',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputSelect',
                        label: 'FarmingOrder',
                        field: 'FarmingOrder',
                        options: [
                            { value: 0, name: 'Unordered' },
                            { value: 1, name: 'AppIDsAscending' },
                            { value: 2, name: 'AppIDsDescending' },
                            { value: 3, name: 'CardDropsAscending' },
                            { value: 4, name: 'CardDropsDescending' },
                            { value: 5, name: 'HoursAscending' },
                            { value: 6, name: 'HoursDescending' },
                            { value: 7, name: 'NamesAscending' },
                            { value: 8, name: 'NamesDescending' },
                            { value: 9, name: 'Random' },
                            { value: 10, name: 'BadgeLevelsAscending' },
                            { value: 11, name: 'BadgeLevelsDescending' },
                            { value: 12, name: 'RedeemDateTimesAscending' },
                            { value: 13, name: 'RedeemDateTimesDescending' },
                            { value: 14, name: 'MarketableAscending' },
                            { value: 15, name: 'MarketableDescending' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputNumber',
                        label: 'SendTradePeriod',
                        field: 'SendTradePeriod',
                        placeholder: 0,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AutoSteamSaleEvent',
                        field: 'AutoSteamSaleEvent',
                        defaultValue: false
                    },
                    {
                        type: 'InputSelect',
                        label: 'OnlineStatus',
                        field: 'OnlineStatus',
                        options: [
                            { value: 0, name: 'Offline' },
                            { value: 1, name: 'Online' },
                            { value: 2, name: 'Busy' },
                            { value: 3, name: 'Away' },
                            { value: 4, name: 'Snooze' },
                            { value: 5, name: 'LookingToTrade' },
                            { value: 6, name: 'LookingToPlay' },
                            { value: 7, name: 'Invisible' }
                        ],
                        defaultValue: 1
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'HandleOfflineMessages',
                        field: 'HandleOfflineMessages',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IdlePriorityQueueOnly',
                        field: 'IdlePriorityQueueOnly',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IdleRefundableGames',
                        field: 'IdleRefundableGames',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'SendOnFarmingFinished',
                        field: 'SendOnFarmingFinished',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'ShutdownOnFarmingFinished',
                        field: 'ShutdownOnFarmingFinished',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.customization',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'DismissInventoryNotifications',
                        field: 'DismissInventoryNotifications',
                        defaultValue: false
                    },
                    {
                        type: 'InputFlag',
                        label: 'RedeemingPreferences',
                        field: 'RedeemingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Forwarding' },
                            { value: 2, name: 'Distributing' },
                            { value: 4, name: 'KeepMissingGames' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'GamesPlayedWhileIdle',
                        field: 'GamesPlayedWhileIdle',
                        validator: Validators.uint
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileFarming',
                        field: 'CustomGamePlayedWhileFarming'
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileIdle',
                        field: 'CustomGamePlayedWhileIdle'
                    }
                ]
            }
        ]
    },
    'V3.1.2.5-V3.1.3.4': {
        asf: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        label: 'SteamOwnerID',
                        field: 's_SteamOwnerID',
                        placeholder: '0',
                        type: 'InputText',
                        description: 'schema.generic.steamid64',
                        validator: Validators.steamid
                    }
                ]
            },
            {
                legend: 'schema.trading',
                advanced: true,
                fields: [
                    {
                        label: 'MaxTradeHoldDuration',
                        field: 'MaxTradeHoldDuration',
                        placeholder: 15,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.customization',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'AutoRestart',
                        label: 'AutoRestart',
                        defaultValue: true
                    },
                    {
                        label: 'Blacklist',
                        field: 'Blacklist',
                        type: 'InputSet',
                        validator: Validators.uint
                    },
                    {
                        field: 'CommandPrefix',
                        label: 'CommandPrefix',
                        type: 'InputText',
                        placeholder: '!'
                    },
                    {
                        label: 'CurrentCulture',
                        field: 'CurrentCulture',
                        type: 'InputText',
                        placeholder: 'en-US'
                    },
                    {
                        type: 'InputCheckbox',
                        field: 'Statistics',
                        label: 'Statistics',
                        defaultValue: true
                    }
                ]
            },
            {
                legend: 'schema.remote_access',
                advanced: true,
                fields: [
                    {
                        label: 'Headless',
                        field: 'Headless',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'IPC',
                        field: 'IPC',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'IPCPassword',
                        field: 'IPCPassword',
                        placeholder: '',
                        type: 'InputPassword'
                    },
                    {
                        label: 'IPCPrefixes',
                        field: 'IPCPrefixes',
                        type: 'InputSet'
                    }
                ]
            },
            {
                legend: 'schema.connection',
                advanced: true,
                fields: [
                    {
                        label: 'ConnectionTimeout',
                        field: 'ConnectionTimeout',
                        placeholder: 60,
                        type: 'InputNumber',
                        validator: Validators.byte
                    },
                    {
                        type: 'InputFlag',
                        label: 'SteamProtocols',
                        field: 'SteamProtocols',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'TCP' },
                            { value: 2, name: 'UDP' },
                            { value: 4, name: 'WebSocket' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        label: 'WebProxy',
                        field: 'WebProxy',
                        placeholder: '',
                        type: 'InputText'
                    },
                    {
                        label: 'WebProxyPassword',
                        field: 'WebProxyPassword',
                        placeholder: '',
                        type: 'InputPassword'
                    },
                    {
                        label: 'WebProxyUsername',
                        field: 'WebProxyUsername',
                        placeholder: '',
                        type: 'InputText'
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        label: 'ConfirmationsLimiterDelay',
                        field: 'ConfirmationsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'FarmingDelay',
                        field: 'FarmingDelay',
                        type: 'InputNumber',
                        placeholder: 15,
                        validator: Validators.byte
                    },
                    {
                        label: 'GiftsLimiterDelay',
                        field: 'GiftsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 1,
                        validator: Validators.byte
                    },
                    {
                        label: 'IdleFarmingPeriod',
                        field: 'IdleFarmingPeriod',
                        type: 'InputNumber',
                        placeholder: 8,
                        validator: Validators.byte
                    },
                    {
                        label: 'InventoryLimiterDelay',
                        field: 'InventoryLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'LoginLimiterDelay',
                        field: 'LoginLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'MaxFarmingTime',
                        field: 'MaxFarmingTime',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'OptimizationMode',
                        field: 'OptimizationMode',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'MaxPerformance' },
                            { value: 1, name: 'MinMemoryUsage' }
                        ],
                        defaultValue: 0
                    },
                    {
                        label: 'WebLimiterDelay',
                        field: 'WebLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 200,
                        validator: Validators.ushort
                    }
                ]
            },
            {
                legend: 'schema.updates',
                advanced: true,
                fields: [
                    {
                        label: 'UpdateChannel',
                        field: 'UpdateChannel',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Stable' },
                            { value: 2, name: 'Experimental' }
                        ],
                        defaultValue: 1
                    },
                    {
                        label: 'UpdatePeriod',
                        field: 'UpdatePeriod',
                        type: 'InputNumber',
                        placeholder: 24,
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.advanced',
                advanced: true,
                fields: [
                    {
                        label: 'Debug',
                        field: 'Debug',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'BackgroundGCPeriod',
                        field: 'BackgroundGCPeriod',
                        type: 'InputNumber',
                        placeholder: 0,
                        validator: Validators.byte
                    }
                ]
            }
        ],
        bot: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        type: 'InputText',
                        label: 'Name',
                        field: 'name',
                        required: true,
                        description: 'schema.bot.name.description'
                    },
                    {
                        type: 'InputText',
                        label: 'SteamLogin',
                        field: 'SteamLogin',
                        description: 'schema.bot.SteamLogin.description'
                    },
                    {
                        type: 'InputPassword',
                        label: 'SteamPassword',
                        field: 'SteamPassword',
                        description: 'schema.bot.SteamPassword.description'
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Enabled',
                        field: 'Enabled',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Paused',
                        field: 'Paused',
                        defaultValue: false,
                        advanced: true
                    },
                    {
                        type: 'InputText',
                        label: 'SteamParentalPIN',
                        field: 'SteamParentalPIN',
                        placeholder: 0,
                        validator: Validators.parentalPIN,
                        advanced: true
                    },
                    {
                        type: 'InputFlag',
                        label: 'BotBehaviour',
                        field: 'BotBehaviour',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'RejectInvalidFriendInvites' },
                            { value: 2, name: 'RejectInvalidTrades' },
                            { value: 4, name: 'RejectInvalidGroupInvites' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.access',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamMasterClanID',
                        field: 's_SteamMasterClanID',
                        placeholder: 0,
                        validator: Validators.masterClan
                    },
                    {
                        type: 'InputMap',
                        label: 'SteamUserPermissions',
                        field: 'SteamUserPermissions',
                        keyPlaceholder: 'SteamID64',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'FamilySharing' },
                            { value: 2, name: 'Operator' },
                            { value: 3, name: 'Master' }
                        ],
                        defaultValue: 0,
                        keyValidator: Validators.steamid
                    }
                ]
            },
            {
                legend: 'schema.trading',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'AcceptGifts',
                        field: 'AcceptGifts',
                        defaultValue: false
                    },
                    {
                        type: 'InputText',
                        label: 'SteamTradeToken',
                        field: 'SteamTradeToken',
                        validator: Validators.tradeToken
                    },
                    {
                        type: 'InputFlag',
                        label: 'TradingPreferences',
                        field: 'TradingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'AcceptDonations' },
                            { value: 2, name: 'SteamTradeMatcher' },
                            { value: 4, name: 'MatchEverything' },
                            { value: 8, name: 'DontAcceptBotTrades' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'LootableTypes',
                        field: 'LootableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'MatchableTypes',
                        field: 'MatchableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.farming',
                advanced: true,
                fields: [
                    {
                        label: 'HoursUntilCardDrops',
                        field: 'HoursUntilCardDrops',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputSelect',
                        label: 'FarmingOrder',
                        field: 'FarmingOrder',
                        options: [
                            { value: 0, name: 'Unordered' },
                            { value: 1, name: 'AppIDsAscending' },
                            { value: 2, name: 'AppIDsDescending' },
                            { value: 3, name: 'CardDropsAscending' },
                            { value: 4, name: 'CardDropsDescending' },
                            { value: 5, name: 'HoursAscending' },
                            { value: 6, name: 'HoursDescending' },
                            { value: 7, name: 'NamesAscending' },
                            { value: 8, name: 'NamesDescending' },
                            { value: 9, name: 'Random' },
                            { value: 10, name: 'BadgeLevelsAscending' },
                            { value: 11, name: 'BadgeLevelsDescending' },
                            { value: 12, name: 'RedeemDateTimesAscending' },
                            { value: 13, name: 'RedeemDateTimesDescending' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputNumber',
                        label: 'SendTradePeriod',
                        field: 'SendTradePeriod',
                        placeholder: 0,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AutoSteamSaleEvent',
                        field: 'AutoSteamSaleEvent',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'FarmOffline',
                        field: 'FarmOffline',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'HandleOfflineMessages',
                        field: 'HandleOfflineMessages',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IdlePriorityQueueOnly',
                        field: 'IdlePriorityQueueOnly',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IdleRefundableGames',
                        field: 'IdleRefundableGames',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'SendOnFarmingFinished',
                        field: 'SendOnFarmingFinished',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'ShutdownOnFarmingFinished',
                        field: 'ShutdownOnFarmingFinished',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.customization',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'DismissInventoryNotifications',
                        field: 'DismissInventoryNotifications',
                        defaultValue: false
                    },
                    {
                        type: 'InputFlag',
                        label: 'RedeemingPreferences',
                        field: 'RedeemingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Forwarding' },
                            { value: 2, name: 'Distributing' },
                            { value: 4, name: 'KeepMissingGames' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'GamesPlayedWhileIdle',
                        field: 'GamesPlayedWhileIdle',
                        validator: Validators.uint
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileFarming',
                        field: 'CustomGamePlayedWhileFarming'
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileIdle',
                        field: 'CustomGamePlayedWhileIdle'
                    }
                ]
            }
        ]
    },
    'V3.1.1.3-V3.1.2.0': {
        asf: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        label: 'SteamOwnerID',
                        field: 's_SteamOwnerID',
                        placeholder: '0',
                        type: 'InputText',
                        description: 'schema.generic.steamid64',
                        validator: Validators.steamid
                    }
                ]
            },
            {
                legend: 'schema.misc',
                advanced: true,
                fields: [
                    {
                        field: 'CommandPrefix',
                        label: 'CommandPrefix',
                        type: 'InputText',
                        placeholder: '!'
                    },
                    {
                        type: 'InputCheckbox',
                        field: 'Statistics',
                        label: 'Statistics',
                        defaultValue: true
                    },
                    {
                        label: 'Blacklist',
                        field: 'Blacklist',
                        type: 'InputSet',
                        validator: Validators.uint
                    },
                    {
                        label: 'CurrentCulture',
                        field: 'CurrentCulture',
                        type: 'InputText',
                        placeholder: 'en-US'
                    },
                    {
                        label: 'MaxTradeHoldDuration',
                        field: 'MaxTradeHoldDuration',
                        placeholder: 15,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.updates',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'AutoRestart',
                        label: 'AutoRestart',
                        defaultValue: true
                    },
                    {
                        label: 'UpdateChannel',
                        field: 'UpdateChannel',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Stable' },
                            { value: 2, name: 'Experimental' }
                        ],
                        defaultValue: 1
                    },
                    {
                        label: 'UpdatePeriod',
                        field: 'UpdatePeriod',
                        type: 'InputNumber',
                        placeholder: 24,
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.remote_access',
                advanced: true,
                fields: [
                    {
                        label: 'IPCPassword',
                        field: 'IPCPassword',
                        placeholder: '',
                        type: 'InputPassword'
                    },
                    {
                        label: 'IPCPrefixes',
                        field: 'IPCPrefixes',
                        type: 'InputSet'
                    },
                    {
                        label: 'Headless',
                        field: 'Headless',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    }
                ]
            },
            {
                legend: 'schema.connection',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'SteamProtocols',
                        field: 'SteamProtocols',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'TCP' },
                            { value: 2, name: 'UDP' },
                            { value: 4, name: 'WebSocket' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        label: 'ConnectionTimeout',
                        field: 'ConnectionTimeout',
                        placeholder: 60,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        label: 'ConfirmationsLimiterDelay',
                        field: 'ConfirmationsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'FarmingDelay',
                        field: 'FarmingDelay',
                        type: 'InputNumber',
                        placeholder: 15,
                        validator: Validators.byte
                    },
                    {
                        label: 'GiftsLimiterDelay',
                        field: 'GiftsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 1,
                        validator: Validators.byte
                    },
                    {
                        label: 'IdleFarmingPeriod',
                        field: 'IdleFarmingPeriod',
                        type: 'InputNumber',
                        placeholder: 8,
                        validator: Validators.byte
                    },
                    {
                        label: 'InventoryLimiterDelay',
                        field: 'InventoryLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'LoginLimiterDelay',
                        field: 'LoginLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'MaxFarmingTime',
                        field: 'MaxFarmingTime',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'OptimizationMode',
                        field: 'OptimizationMode',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'MaxPerformance' },
                            { value: 1, name: 'MinMemoryUsage' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.advanced',
                advanced: true,
                fields: [
                    {
                        label: 'Debug',
                        field: 'Debug',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'BackgroundGCPeriod',
                        field: 'BackgroundGCPeriod',
                        type: 'InputNumber',
                        placeholder: 0,
                        validator: Validators.byte
                    }
                ]
            }
        ],
        bot: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        type: 'InputText',
                        label: 'Name',
                        field: 'name',
                        required: true,
                        description: 'schema.bot.name.description'
                    },
                    {
                        type: 'InputText',
                        label: 'SteamLogin',
                        field: 'SteamLogin',
                        description: 'schema.bot.SteamLogin.description'
                    },
                    {
                        type: 'InputPassword',
                        label: 'SteamPassword',
                        field: 'SteamPassword',
                        description: 'schema.bot.SteamPassword.description'
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Enabled',
                        field: 'Enabled',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IsBotAccount',
                        field: 'IsBotAccount',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Paused',
                        field: 'Paused',
                        defaultValue: false,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.security',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'PasswordFormat',
                        field: 'PasswordFormat',
                        options: [
                            { value: 0, name: 'PlainText' },
                            { value: 1, name: 'AES' },
                            { value: 2, name: 'ProtectedDataForCurrentUser' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'UseLoginKeys',
                        field: 'UseLoginKeys',
                        defaultValue: true,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.access',
                advanced: true,
                fields: [
                    {
                        type: 'InputMap',
                        label: 'SteamUserPermissions',
                        field: 'SteamUserPermissions',
                        keyPlaceholder: 'SteamID64',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'FamilySharing' },
                            { value: 2, name: 'Operator' },
                            { value: 3, name: 'Master' }
                        ],
                        defaultValue: 0,
                        keyValidator: Validators.steamid
                    },
                    {
                        type: 'InputText',
                        label: 'SteamParentalPIN',
                        field: 'SteamParentalPIN',
                        placeholder: 0,
                        validator: Validators.parentalPIN
                    }
                ]
            },
            {
                legend: 'schema.community',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'HandleOfflineMessages',
                        field: 'HandleOfflineMessages',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.trading',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamTradeToken',
                        field: 'SteamTradeToken',
                        validator: Validators.tradeToken
                    },
                    {
                        type: 'InputFlag',
                        label: 'TradingPreferences',
                        field: 'TradingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'AcceptDonations' },
                            { value: 2, name: 'SteamTradeMatcher' },
                            { value: 4, name: 'MatchEverything' },
                            { value: 8, name: 'DontAcceptBotTrades' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'LootableTypes',
                        field: 'LootableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'MatchableTypes',
                        field: 'MatchableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AcceptGifts',
                        field: 'AcceptGifts',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'DismissInventoryNotifications',
                        field: 'DismissInventoryNotifications',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.farming',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'FarmingOrder',
                        field: 'FarmingOrder',
                        options: [
                            { value: 0, name: 'Unordered' },
                            { value: 1, name: 'AppIDsAscending' },
                            { value: 2, name: 'AppIDsDescending' },
                            { value: 3, name: 'CardDropsAscending' },
                            { value: 4, name: 'CardDropsDescending' },
                            { value: 5, name: 'HoursAscending' },
                            { value: 6, name: 'HoursDescending' },
                            { value: 7, name: 'NamesAscending' },
                            { value: 8, name: 'NamesDescending' },
                            { value: 9, name: 'Random' },
                            { value: 10, name: 'BadgeLevelsAscending' },
                            { value: 11, name: 'BadgeLevelsDescending' },
                            { value: 12, name: 'RedeemDateTimesAscending' },
                            { value: 13, name: 'RedeemDateTimesDescending' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputNumber',
                        label: 'SendTradePeriod',
                        field: 'SendTradePeriod',
                        placeholder: 0,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AutoSteamSaleEvent',
                        field: 'AutoSteamSaleEvent',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IdlePriorityQueueOnly',
                        field: 'IdlePriorityQueueOnly',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IdleRefundableGames',
                        field: 'IdleRefundableGames',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'FarmOffline',
                        field: 'FarmOffline',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'SendOnFarmingFinished',
                        field: 'SendOnFarmingFinished',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'ShutdownOnFarmingFinished',
                        field: 'ShutdownOnFarmingFinished',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.customization',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamMasterClanID',
                        field: 's_SteamMasterClanID',
                        placeholder: 0,
                        validator: Validators.masterClan
                    },
                    {
                        type: 'InputSet',
                        label: 'GamesPlayedWhileIdle',
                        field: 'GamesPlayedWhileIdle',
                        validator: Validators.uint
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileFarming',
                        field: 'CustomGamePlayedWhileFarming'
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileIdle',
                        field: 'CustomGamePlayedWhileIdle'
                    }
                ]
            },
            {
                legend: 'schema.misc',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'RedeemingPreferences',
                        field: 'RedeemingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Forwarding' },
                            { value: 2, name: 'Distributing' },
                            { value: 4, name: 'KeepMissingGames' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        label: 'HoursUntilCardDrops',
                        field: 'HoursUntilCardDrops',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    }
                ]
            }
        ]
    },
    'V3.1.0.9-V3.1.1.2': {
        asf: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        label: 'SteamOwnerID',
                        field: 's_SteamOwnerID',
                        placeholder: '0',
                        type: 'InputText',
                        description: 'schema.generic.steamid64',
                        validator: Validators.steamid
                    }
                ]
            },
            {
                legend: 'schema.misc',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'Statistics',
                        label: 'Statistics',
                        defaultValue: true
                    },
                    {
                        label: 'Blacklist',
                        field: 'Blacklist',
                        type: 'InputSet',
                        validator: Validators.uint
                    },
                    {
                        label: 'CurrentCulture',
                        field: 'CurrentCulture',
                        type: 'InputText',
                        placeholder: 'en-US'
                    },
                    {
                        label: 'MaxTradeHoldDuration',
                        field: 'MaxTradeHoldDuration',
                        placeholder: 15,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.updates',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'AutoRestart',
                        label: 'AutoRestart',
                        defaultValue: true
                    },
                    {
                        label: 'UpdateChannel',
                        field: 'UpdateChannel',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Stable' },
                            { value: 2, name: 'Experimental' }
                        ],
                        defaultValue: 1
                    },
                    {
                        label: 'UpdatePeriod',
                        field: 'UpdatePeriod',
                        type: 'InputNumber',
                        placeholder: 24,
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.remote_access',
                advanced: true,
                fields: [
                    {
                        label: 'IPCPassword',
                        field: 'IPCPassword',
                        placeholder: '',
                        type: 'InputPassword'
                    },
                    {
                        label: 'IPCPrefixes',
                        field: 'IPCPrefixes',
                        type: 'InputSet'
                    },
                    {
                        label: 'Headless',
                        field: 'Headless',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    }
                ]
            },
            {
                legend: 'schema.connection',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'SteamProtocols',
                        field: 'SteamProtocols',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'TCP' },
                            { value: 2, name: 'UDP' },
                            { value: 4, name: 'WebSocket' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        label: 'ConnectionTimeout',
                        field: 'ConnectionTimeout',
                        placeholder: 60,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        label: 'ConfirmationsLimiterDelay',
                        field: 'ConfirmationsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'FarmingDelay',
                        field: 'FarmingDelay',
                        type: 'InputNumber',
                        placeholder: 15,
                        validator: Validators.byte
                    },
                    {
                        label: 'GiftsLimiterDelay',
                        field: 'GiftsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 1,
                        validator: Validators.byte
                    },
                    {
                        label: 'IdleFarmingPeriod',
                        field: 'IdleFarmingPeriod',
                        type: 'InputNumber',
                        placeholder: 8,
                        validator: Validators.byte
                    },
                    {
                        label: 'InventoryLimiterDelay',
                        field: 'InventoryLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'LoginLimiterDelay',
                        field: 'LoginLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'MaxFarmingTime',
                        field: 'MaxFarmingTime',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'OptimizationMode',
                        field: 'OptimizationMode',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'MaxPerformance' },
                            { value: 1, name: 'MinMemoryUsage' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.advanced',
                advanced: true,
                fields: [
                    {
                        label: 'Debug',
                        field: 'Debug',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'BackgroundGCPeriod',
                        field: 'BackgroundGCPeriod',
                        type: 'InputNumber',
                        placeholder: 0,
                        validator: Validators.byte
                    }
                ]
            }
        ],
        bot: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        type: 'InputText',
                        label: 'Name',
                        field: 'name',
                        required: true,
                        description: 'schema.bot.name.description'
                    },
                    {
                        type: 'InputText',
                        label: 'SteamLogin',
                        field: 'SteamLogin',
                        description: 'schema.bot.SteamLogin.description'
                    },
                    {
                        type: 'InputPassword',
                        label: 'SteamPassword',
                        field: 'SteamPassword',
                        description: 'schema.bot.SteamPassword.description'
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Enabled',
                        field: 'Enabled',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IsBotAccount',
                        field: 'IsBotAccount',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Paused',
                        field: 'Paused',
                        defaultValue: false,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.security',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'PasswordFormat',
                        field: 'PasswordFormat',
                        options: [
                            { value: 0, name: 'PlainText' },
                            { value: 1, name: 'AES' },
                            { value: 2, name: 'ProtectedDataForCurrentUser' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'UseLoginKeys',
                        field: 'UseLoginKeys',
                        defaultValue: true,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.access',
                advanced: true,
                fields: [
                    {
                        type: 'InputMap',
                        label: 'SteamUserPermissions',
                        field: 'SteamUserPermissions',
                        keyPlaceholder: 'SteamID64',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'FamilySharing' },
                            { value: 2, name: 'Operator' },
                            { value: 3, name: 'Master' }
                        ],
                        defaultValue: 0,
                        keyValidator: Validators.steamid
                    },
                    {
                        type: 'InputText',
                        label: 'SteamParentalPIN',
                        field: 'SteamParentalPIN',
                        placeholder: 0,
                        validator: Validators.parentalPIN
                    }
                ]
            },
            {
                legend: 'schema.community',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'HandleOfflineMessages',
                        field: 'HandleOfflineMessages',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.trading',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamTradeToken',
                        field: 'SteamTradeToken',
                        validator: Validators.tradeToken
                    },
                    {
                        type: 'InputFlag',
                        label: 'TradingPreferences',
                        field: 'TradingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'AcceptDonations' },
                            { value: 2, name: 'SteamTradeMatcher' },
                            { value: 4, name: 'MatchEverything' },
                            { value: 8, name: 'DontAcceptBotTrades' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'LootableTypes',
                        field: 'LootableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'MatchableTypes',
                        field: 'MatchableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AcceptGifts',
                        field: 'AcceptGifts',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'DismissInventoryNotifications',
                        field: 'DismissInventoryNotifications',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.farming',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'FarmingOrder',
                        field: 'FarmingOrder',
                        options: [
                            { value: 0, name: 'Unordered' },
                            { value: 1, name: 'AppIDsAscending' },
                            { value: 2, name: 'AppIDsDescending' },
                            { value: 3, name: 'CardDropsAscending' },
                            { value: 4, name: 'CardDropsDescending' },
                            { value: 5, name: 'HoursAscending' },
                            { value: 6, name: 'HoursDescending' },
                            { value: 7, name: 'NamesAscending' },
                            { value: 8, name: 'NamesDescending' },
                            { value: 9, name: 'Random' },
                            { value: 10, name: 'BadgeLevelsAscending' },
                            { value: 11, name: 'BadgeLevelsDescending' },
                            { value: 12, name: 'RedeemDateTimesAscending' },
                            { value: 13, name: 'RedeemDateTimesDescending' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputNumber',
                        label: 'SendTradePeriod',
                        field: 'SendTradePeriod',
                        placeholder: 0,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AutoSteamSaleEvent',
                        field: 'AutoSteamSaleEvent',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IdlePriorityQueueOnly',
                        field: 'IdlePriorityQueueOnly',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IdleRefundableGames',
                        field: 'IdleRefundableGames',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'FarmOffline',
                        field: 'FarmOffline',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'SendOnFarmingFinished',
                        field: 'SendOnFarmingFinished',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'ShutdownOnFarmingFinished',
                        field: 'ShutdownOnFarmingFinished',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.customization',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamMasterClanID',
                        field: 's_SteamMasterClanID',
                        placeholder: 0,
                        validator: Validators.masterClan
                    },
                    {
                        type: 'InputSet',
                        label: 'GamesPlayedWhileIdle',
                        field: 'GamesPlayedWhileIdle',
                        validator: Validators.uint
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileFarming',
                        field: 'CustomGamePlayedWhileFarming'
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileIdle',
                        field: 'CustomGamePlayedWhileIdle'
                    }
                ]
            },
            {
                legend: 'schema.misc',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'RedeemingPreferences',
                        field: 'RedeemingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Forwarding' },
                            { value: 2, name: 'Distributing' },
                            { value: 4, name: 'KeepMissingGames' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        label: 'HoursUntilCardDrops',
                        field: 'HoursUntilCardDrops',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    }
                ]
            }
        ]
    },
    'V3.0.5.8-V3.1.0.1': {
        asf: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        label: 'SteamOwnerID',
                        field: 's_SteamOwnerID',
                        placeholder: '0',
                        type: 'InputText',
                        description: 'schema.generic.steamid64',
                        validator: Validators.steamid
                    }
                ]
            },
            {
                legend: 'schema.misc',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'Statistics',
                        label: 'Statistics',
                        defaultValue: true
                    },
                    {
                        label: 'Blacklist',
                        field: 'Blacklist',
                        type: 'InputSet',
                        validator: Validators.uint
                    },
                    {
                        label: 'CurrentCulture',
                        field: 'CurrentCulture',
                        type: 'InputText',
                        placeholder: 'en-US'
                    },
                    {
                        label: 'MaxTradeHoldDuration',
                        field: 'MaxTradeHoldDuration',
                        placeholder: 15,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.updates',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'AutoRestart',
                        label: 'AutoRestart',
                        defaultValue: true
                    },
                    {
                        label: 'UpdateChannel',
                        field: 'UpdateChannel',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Stable' },
                            { value: 2, name: 'Experimental' }
                        ],
                        defaultValue: 1
                    },
                    {
                        label: 'UpdatePeriod',
                        field: 'UpdatePeriod',
                        type: 'InputNumber',
                        placeholder: 24,
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.remote_access',
                advanced: true,
                fields: [
                    {
                        label: 'IPCHost',
                        field: 'IPCHost',
                        placeholder: '127.0.0.1',
                        type: 'InputText'
                    },
                    {
                        label: 'IPCPassword',
                        field: 'IPCPassword',
                        placeholder: '',
                        type: 'InputPassword'
                    },
                    {
                        label: 'IPCPort',
                        field: 'IPCPort',
                        placeholder: 1242,
                        type: 'InputNumber',
                        validator: Validators.ushort
                    },
                    {
                        label: 'Headless',
                        field: 'Headless',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    }
                ]
            },
            {
                legend: 'schema.connection',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'SteamProtocols',
                        field: 'SteamProtocols',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'TCP' },
                            { value: 2, name: 'UDP' },
                            { value: 4, name: 'WebSocket' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        label: 'ConnectionTimeout',
                        field: 'ConnectionTimeout',
                        placeholder: 60,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        label: 'ConfirmationsLimiterDelay',
                        field: 'ConfirmationsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'FarmingDelay',
                        field: 'FarmingDelay',
                        type: 'InputNumber',
                        placeholder: 15,
                        validator: Validators.byte
                    },
                    {
                        label: 'GiftsLimiterDelay',
                        field: 'GiftsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 1,
                        validator: Validators.byte
                    },
                    {
                        label: 'IdleFarmingPeriod',
                        field: 'IdleFarmingPeriod',
                        type: 'InputNumber',
                        placeholder: 8,
                        validator: Validators.byte
                    },
                    {
                        label: 'InventoryLimiterDelay',
                        field: 'InventoryLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'LoginLimiterDelay',
                        field: 'LoginLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'MaxFarmingTime',
                        field: 'MaxFarmingTime',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'OptimizationMode',
                        field: 'OptimizationMode',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'MaxPerformance' },
                            { value: 1, name: 'MinMemoryUsage' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.advanced',
                advanced: true,
                fields: [
                    {
                        label: 'Debug',
                        field: 'Debug',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'BackgroundGCPeriod',
                        field: 'BackgroundGCPeriod',
                        type: 'InputNumber',
                        placeholder: 0,
                        validator: Validators.byte
                    }
                ]
            }
        ],
        bot: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        type: 'InputText',
                        label: 'Name',
                        field: 'name',
                        required: true,
                        description: 'schema.bot.name.description'
                    },
                    {
                        type: 'InputText',
                        label: 'SteamLogin',
                        field: 'SteamLogin',
                        description: 'schema.bot.SteamLogin.description'
                    },
                    {
                        type: 'InputPassword',
                        label: 'SteamPassword',
                        field: 'SteamPassword',
                        description: 'schema.bot.SteamPassword.description'
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Enabled',
                        field: 'Enabled',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IsBotAccount',
                        field: 'IsBotAccount',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Paused',
                        field: 'Paused',
                        defaultValue: false,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.security',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'PasswordFormat',
                        field: 'PasswordFormat',
                        options: [
                            { value: 0, name: 'PlainText' },
                            { value: 1, name: 'AES' },
                            { value: 2, name: 'ProtectedDataForCurrentUser' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'UseLoginKeys',
                        field: 'UseLoginKeys',
                        defaultValue: true,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.access',
                advanced: true,
                fields: [
                    {
                        type: 'InputMap',
                        label: 'SteamUserPermissions',
                        field: 'SteamUserPermissions',
                        keyPlaceholder: 'SteamID64',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'FamilySharing' },
                            { value: 2, name: 'Operator' },
                            { value: 3, name: 'Master' }
                        ],
                        defaultValue: 0,
                        keyValidator: Validators.steamid
                    },
                    {
                        type: 'InputText',
                        label: 'SteamParentalPIN',
                        field: 'SteamParentalPIN',
                        placeholder: 0,
                        validator: Validators.parentalPIN
                    }
                ]
            },
            {
                legend: 'schema.community',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'HandleOfflineMessages',
                        field: 'HandleOfflineMessages',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.trading',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamTradeToken',
                        field: 'SteamTradeToken',
                        validator: Validators.tradeToken
                    },
                    {
                        type: 'InputFlag',
                        label: 'TradingPreferences',
                        field: 'TradingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'AcceptDonations' },
                            { value: 2, name: 'SteamTradeMatcher' },
                            { value: 4, name: 'MatchEverything' },
                            { value: 8, name: 'DontAcceptBotTrades' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'LootableTypes',
                        field: 'LootableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'MatchableTypes',
                        field: 'MatchableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AcceptGifts',
                        field: 'AcceptGifts',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'DismissInventoryNotifications',
                        field: 'DismissInventoryNotifications',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.farming',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'FarmingOrder',
                        field: 'FarmingOrder',
                        options: [
                            { value: 0, name: 'Unordered' },
                            { value: 1, name: 'AppIDsAscending' },
                            { value: 2, name: 'AppIDsDescending' },
                            { value: 3, name: 'CardDropsAscending' },
                            { value: 4, name: 'CardDropsDescending' },
                            { value: 5, name: 'HoursAscending' },
                            { value: 6, name: 'HoursDescending' },
                            { value: 7, name: 'NamesAscending' },
                            { value: 8, name: 'NamesDescending' },
                            { value: 9, name: 'Random' },
                            { value: 10, name: 'BadgeLevelsAscending' },
                            { value: 11, name: 'BadgeLevelsDescending' },
                            { value: 12, name: 'RedeemDateTimesAscending' },
                            { value: 13, name: 'RedeemDateTimesDescending' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputNumber',
                        label: 'SendTradePeriod',
                        field: 'SendTradePeriod',
                        placeholder: 0,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AutoSteamSaleEvent',
                        field: 'AutoSteamSaleEvent',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IdleRefundableGames',
                        field: 'IdleRefundableGames',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'FarmOffline',
                        field: 'FarmOffline',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'SendOnFarmingFinished',
                        field: 'SendOnFarmingFinished',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'ShutdownOnFarmingFinished',
                        field: 'ShutdownOnFarmingFinished',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.customization',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamMasterClanID',
                        field: 's_SteamMasterClanID',
                        placeholder: 0,
                        validator: Validators.masterClan
                    },
                    {
                        type: 'InputSet',
                        label: 'GamesPlayedWhileIdle',
                        field: 'GamesPlayedWhileIdle',
                        validator: Validators.uint
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileFarming',
                        field: 'CustomGamePlayedWhileFarming'
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileIdle',
                        field: 'CustomGamePlayedWhileIdle'
                    }
                ]
            },
            {
                legend: 'schema.misc',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'RedeemingPreferences',
                        field: 'RedeemingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Forwarding' },
                            { value: 2, name: 'Distributing' },
                            { value: 4, name: 'KeepMissingGames' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        label: 'HoursUntilCardDrops',
                        field: 'HoursUntilCardDrops',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    }
                ]
            }
        ]
    },
    'V3.0.5.0-V3.0.5.5': {
        asf: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        label: 'SteamOwnerID',
                        field: 's_SteamOwnerID',
                        placeholder: '0',
                        type: 'InputText',
                        description: 'schema.generic.steamid64',
                        validator: Validators.steamid
                    }
                ]
            },
            {
                legend: 'schema.misc',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'Statistics',
                        label: 'Statistics',
                        defaultValue: true
                    },
                    {
                        label: 'Blacklist',
                        field: 'Blacklist',
                        type: 'InputSet',
                        validator: Validators.uint
                    },
                    {
                        label: 'CurrentCulture',
                        field: 'CurrentCulture',
                        type: 'InputText',
                        placeholder: 'en-US'
                    },
                    {
                        label: 'MaxTradeHoldDuration',
                        field: 'MaxTradeHoldDuration',
                        placeholder: 15,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.updates',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'AutoRestart',
                        label: 'AutoRestart',
                        defaultValue: true
                    },
                    {
                        label: 'UpdateChannel',
                        field: 'UpdateChannel',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Stable' },
                            { value: 2, name: 'Experimental' }
                        ],
                        defaultValue: 1
                    },
                    {
                        label: 'UpdatePeriod',
                        field: 'UpdatePeriod',
                        type: 'InputNumber',
                        placeholder: 24,
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.remote_access',
                advanced: true,
                fields: [
                    {
                        label: 'IPCHost',
                        field: 'IPCHost',
                        placeholder: '127.0.0.1',
                        type: 'InputText'
                    },
                    {
                        label: 'IPCPassword',
                        field: 'IPCPassword',
                        placeholder: '',
                        type: 'InputPassword'
                    },
                    {
                        label: 'IPCPort',
                        field: 'IPCPort',
                        placeholder: 1242,
                        type: 'InputNumber',
                        validator: Validators.ushort
                    },
                    {
                        label: 'Headless',
                        field: 'Headless',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    }
                ]
            },
            {
                legend: 'schema.connection',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'SteamProtocols',
                        field: 'SteamProtocols',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'TCP' },
                            { value: 2, name: 'UDP' },
                            { value: 4, name: 'WebSocket' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        label: 'ConnectionTimeout',
                        field: 'ConnectionTimeout',
                        placeholder: 60,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        label: 'FarmingDelay',
                        field: 'FarmingDelay',
                        type: 'InputNumber',
                        placeholder: 15,
                        validator: Validators.byte
                    },
                    {
                        label: 'GiftsLimiterDelay',
                        field: 'GiftsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 1,
                        validator: Validators.byte
                    },
                    {
                        label: 'IdleFarmingPeriod',
                        field: 'IdleFarmingPeriod',
                        type: 'InputNumber',
                        placeholder: 8,
                        validator: Validators.byte
                    },
                    {
                        label: 'InventoryLimiterDelay',
                        field: 'InventoryLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'LoginLimiterDelay',
                        field: 'LoginLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'MaxFarmingTime',
                        field: 'MaxFarmingTime',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'OptimizationMode',
                        field: 'OptimizationMode',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'MaxPerformance' },
                            { value: 1, name: 'MinMemoryUsage' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.advanced',
                advanced: true,
                fields: [
                    {
                        label: 'Debug',
                        field: 'Debug',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'BackgroundGCPeriod',
                        field: 'BackgroundGCPeriod',
                        type: 'InputNumber',
                        placeholder: 0,
                        validator: Validators.byte
                    }
                ]
            }
        ],
        bot: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        type: 'InputText',
                        label: 'Name',
                        field: 'name',
                        required: true,
                        description: 'schema.bot.name.description'
                    },
                    {
                        type: 'InputText',
                        label: 'SteamLogin',
                        field: 'SteamLogin',
                        description: 'schema.bot.SteamLogin.description'
                    },
                    {
                        type: 'InputPassword',
                        label: 'SteamPassword',
                        field: 'SteamPassword',
                        description: 'schema.bot.SteamPassword.description'
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Enabled',
                        field: 'Enabled',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IsBotAccount',
                        field: 'IsBotAccount',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Paused',
                        field: 'Paused',
                        defaultValue: false,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.security',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'PasswordFormat',
                        field: 'PasswordFormat',
                        options: [
                            { value: 0, name: 'PlainText' },
                            { value: 1, name: 'AES' },
                            { value: 2, name: 'ProtectedDataForCurrentUser' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'UseLoginKeys',
                        field: 'UseLoginKeys',
                        defaultValue: true,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.access',
                advanced: true,
                fields: [
                    {
                        type: 'InputMap',
                        label: 'SteamUserPermissions',
                        field: 'SteamUserPermissions',
                        keyPlaceholder: 'SteamID64',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'FamilySharing' },
                            { value: 2, name: 'Operator' },
                            { value: 3, name: 'Master' }
                        ],
                        defaultValue: 0,
                        keyValidator: Validators.steamid
                    },
                    {
                        type: 'InputText',
                        label: 'SteamParentalPIN',
                        field: 'SteamParentalPIN',
                        placeholder: 0,
                        validator: Validators.parentalPIN
                    }
                ]
            },
            {
                legend: 'schema.community',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'HandleOfflineMessages',
                        field: 'HandleOfflineMessages',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.trading',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamTradeToken',
                        field: 'SteamTradeToken',
                        validator: Validators.tradeToken
                    },
                    {
                        type: 'InputFlag',
                        label: 'TradingPreferences',
                        field: 'TradingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'AcceptDonations' },
                            { value: 2, name: 'SteamTradeMatcher' },
                            { value: 4, name: 'MatchEverything' },
                            { value: 8, name: 'DontAcceptBotTrades' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'LootableTypes',
                        field: 'LootableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'MatchableTypes',
                        field: 'MatchableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AcceptGifts',
                        field: 'AcceptGifts',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'DismissInventoryNotifications',
                        field: 'DismissInventoryNotifications',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.farming',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'FarmingOrder',
                        field: 'FarmingOrder',
                        options: [
                            { value: 0, name: 'Unordered' },
                            { value: 1, name: 'AppIDsAscending' },
                            { value: 2, name: 'AppIDsDescending' },
                            { value: 3, name: 'CardDropsAscending' },
                            { value: 4, name: 'CardDropsDescending' },
                            { value: 5, name: 'HoursAscending' },
                            { value: 6, name: 'HoursDescending' },
                            { value: 7, name: 'NamesAscending' },
                            { value: 8, name: 'NamesDescending' },
                            { value: 9, name: 'Random' },
                            { value: 10, name: 'BadgeLevelsAscending' },
                            { value: 11, name: 'BadgeLevelsDescending' },
                            { value: 12, name: 'RedeemDateTimesAscending' },
                            { value: 13, name: 'RedeemDateTimesDescending' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputNumber',
                        label: 'SendTradePeriod',
                        field: 'SendTradePeriod',
                        placeholder: 0,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AutoDiscoveryQueue',
                        field: 'AutoDiscoveryQueue',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IdleRefundableGames',
                        field: 'IdleRefundableGames',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'FarmOffline',
                        field: 'FarmOffline',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'SendOnFarmingFinished',
                        field: 'SendOnFarmingFinished',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'ShutdownOnFarmingFinished',
                        field: 'ShutdownOnFarmingFinished',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.customization',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamMasterClanID',
                        field: 's_SteamMasterClanID',
                        placeholder: 0,
                        validator: Validators.masterClan
                    },
                    {
                        type: 'InputSet',
                        label: 'GamesPlayedWhileIdle',
                        field: 'GamesPlayedWhileIdle',
                        validator: Validators.uint
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileFarming',
                        field: 'CustomGamePlayedWhileFarming'
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileIdle',
                        field: 'CustomGamePlayedWhileIdle'
                    }
                ]
            },
            {
                legend: 'schema.misc',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'RedeemingPreferences',
                        field: 'RedeemingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Forwarding' },
                            { value: 2, name: 'Distributing' },
                            { value: 4, name: 'KeepMissingGames' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        label: 'HoursUntilCardDrops',
                        field: 'HoursUntilCardDrops',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    }
                ]
            }
        ]
    },
    'V3.0.3.7-V3.0.4.8': {
        asf: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        label: 'SteamOwnerID',
                        field: 's_SteamOwnerID',
                        placeholder: '0',
                        type: 'InputText',
                        description: 'schema.generic.steamid64',
                        validator: Validators.steamid
                    }
                ]
            },
            {
                legend: 'schema.misc',
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'Statistics',
                        label: 'Statistics',
                        defaultValue: true,
                        advanced: true
                    },
                    {
                        label: 'Blacklist',
                        field: 'Blacklist',
                        type: 'InputSet',
                        validator: Validators.uint
                    },
                    {
                        label: 'CurrentCulture',
                        field: 'CurrentCulture',
                        type: 'InputText',
                        placeholder: 'en-US',
                        advanced: true
                    },
                    {
                        label: 'MaxTradeHoldDuration',
                        field: 'MaxTradeHoldDuration',
                        placeholder: 15,
                        type: 'InputNumber',
                        advanced: true,
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.updates',
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'AutoUpdates',
                        label: 'AutoUpdates',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        field: 'AutoRestart',
                        label: 'AutoRestart',
                        defaultValue: true,
                        advanced: true
                    },
                    {
                        label: 'UpdateChannel',
                        field: 'UpdateChannel',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Stable' },
                            { value: 2, name: 'Experimental' }
                        ],
                        defaultValue: 1,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.remote_access',
                advanced: true,
                fields: [
                    {
                        label: 'IPCHost',
                        field: 'IPCHost',
                        placeholder: '127.0.0.1',
                        type: 'InputText'
                    },
                    {
                        label: 'IPCPort',
                        field: 'IPCPort',
                        placeholder: 1242,
                        type: 'InputNumber',
                        validator: Validators.ushort
                    },
                    {
                        label: 'Headless',
                        field: 'Headless',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    }
                ]
            },
            {
                legend: 'schema.connection',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'SteamProtocols',
                        field: 'SteamProtocols',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'TCP' },
                            { value: 2, name: 'UDP' },
                            { value: 4, name: 'WebSocket' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        label: 'ConnectionTimeout',
                        field: 'ConnectionTimeout',
                        placeholder: 60,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        label: 'FarmingDelay',
                        field: 'FarmingDelay',
                        type: 'InputNumber',
                        placeholder: 15,
                        validator: Validators.byte
                    },
                    {
                        label: 'GiftsLimiterDelay',
                        field: 'GiftsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 1,
                        validator: Validators.byte
                    },
                    {
                        label: 'IdleFarmingPeriod',
                        field: 'IdleFarmingPeriod',
                        type: 'InputNumber',
                        placeholder: 8,
                        validator: Validators.byte
                    },
                    {
                        label: 'InventoryLimiterDelay',
                        field: 'InventoryLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'LoginLimiterDelay',
                        field: 'LoginLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'MaxFarmingTime',
                        field: 'MaxFarmingTime',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'OptimizationMode',
                        field: 'OptimizationMode',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'MaxPerformance' },
                            { value: 1, name: 'MinMemoryUsage' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.advanced',
                advanced: true,
                fields: [
                    {
                        label: 'Debug',
                        field: 'Debug',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'BackgroundGCPeriod',
                        field: 'BackgroundGCPeriod',
                        type: 'InputNumber',
                        placeholder: 0,
                        validator: Validators.byte
                    }
                ]
            }
        ],
        bot: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        type: 'InputText',
                        label: 'Name',
                        field: 'name',
                        required: true,
                        description: 'schema.bot.name.description'
                    },
                    {
                        type: 'InputText',
                        label: 'SteamLogin',
                        field: 'SteamLogin',
                        description: 'schema.bot.SteamLogin.description'
                    },
                    {
                        type: 'InputPassword',
                        label: 'SteamPassword',
                        field: 'SteamPassword',
                        description: 'schema.bot.SteamPassword.description'
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Enabled',
                        field: 'Enabled',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IsBotAccount',
                        field: 'IsBotAccount',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Paused',
                        field: 'Paused',
                        defaultValue: false,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.security',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'PasswordFormat',
                        field: 'PasswordFormat',
                        options: [
                            { value: 0, name: 'PlainText' },
                            { value: 1, name: 'AES' },
                            { value: 2, name: 'ProtectedDataForCurrentUser' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.access',
                advanced: true,
                fields: [
                    {
                        type: 'InputMap',
                        label: 'SteamUserPermissions',
                        field: 'SteamUserPermissions',
                        keyPlaceholder: 'SteamID64',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'FamilySharing' },
                            { value: 2, name: 'Operator' },
                            { value: 3, name: 'Master' }
                        ],
                        defaultValue: 0,
                        keyValidator: Validators.steamid
                    },
                    {
                        type: 'InputText',
                        label: 'SteamParentalPIN',
                        field: 'SteamParentalPIN',
                        placeholder: 0,
                        validator: Validators.parentalPIN
                    }
                ]
            },
            {
                legend: 'schema.community',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'HandleOfflineMessages',
                        field: 'HandleOfflineMessages',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.trading',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamTradeToken',
                        field: 'SteamTradeToken',
                        validator: Validators.tradeToken
                    },
                    {
                        type: 'InputFlag',
                        label: 'TradingPreferences',
                        field: 'TradingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'AcceptDonations' },
                            { value: 2, name: 'SteamTradeMatcher' },
                            { value: 4, name: 'MatchEverything' },
                            { value: 8, name: 'DontAcceptBotTrades' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'LootableTypes',
                        field: 'LootableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'MatchableTypes',
                        field: 'MatchableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AcceptGifts',
                        field: 'AcceptGifts',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'DismissInventoryNotifications',
                        field: 'DismissInventoryNotifications',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.farming',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'FarmingOrder',
                        field: 'FarmingOrder',
                        options: [
                            { value: 0, name: 'Unordered' },
                            { value: 1, name: 'AppIDsAscending' },
                            { value: 2, name: 'AppIDsDescending' },
                            { value: 3, name: 'CardDropsAscending' },
                            { value: 4, name: 'CardDropsDescending' },
                            { value: 5, name: 'HoursAscending' },
                            { value: 6, name: 'HoursDescending' },
                            { value: 7, name: 'NamesAscending' },
                            { value: 8, name: 'NamesDescending' },
                            { value: 9, name: 'Random' },
                            { value: 10, name: 'BadgeLevelsAscending' },
                            { value: 11, name: 'BadgeLevelsDescending' },
                            { value: 12, name: 'RedeemDateTimesAscending' },
                            { value: 13, name: 'RedeemDateTimesDescending' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputNumber',
                        label: 'SendTradePeriod',
                        field: 'SendTradePeriod',
                        placeholder: 0,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AutoDiscoveryQueue',
                        field: 'AutoDiscoveryQueue',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IdleRefundableGames',
                        field: 'IdleRefundableGames',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'FarmOffline',
                        field: 'FarmOffline',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'SendOnFarmingFinished',
                        field: 'SendOnFarmingFinished',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'ShutdownOnFarmingFinished',
                        field: 'ShutdownOnFarmingFinished',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.customization',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamMasterClanID',
                        field: 's_SteamMasterClanID',
                        placeholder: 0,
                        validator: Validators.masterClan
                    },
                    {
                        type: 'InputSet',
                        label: 'GamesPlayedWhileIdle',
                        field: 'GamesPlayedWhileIdle',
                        validator: Validators.uint
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileFarming',
                        field: 'CustomGamePlayedWhileFarming'
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileIdle',
                        field: 'CustomGamePlayedWhileIdle'
                    }
                ]
            },
            {
                legend: 'schema.misc',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'RedeemingPreferences',
                        field: 'RedeemingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Forwarding' },
                            { value: 2, name: 'Distributing' },
                            { value: 4, name: 'KeepMissingGames' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        label: 'HoursUntilCardDrops',
                        field: 'HoursUntilCardDrops',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    }
                ]
            }
        ]
    },
    'V3.0.1.6-V3.0.3.6': {
        asf: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        label: 'SteamOwnerID',
                        field: 's_SteamOwnerID',
                        placeholder: '0',
                        type: 'InputText',
                        description: 'schema.generic.steamid64',
                        validator: Validators.steamid
                    }
                ]
            },
            {
                legend: 'schema.misc',
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'Statistics',
                        label: 'Statistics',
                        defaultValue: true,
                        advanced: true
                    },
                    {
                        label: 'Blacklist',
                        field: 'Blacklist',
                        type: 'InputSet',
                        validator: Validators.uint
                    },
                    {
                        label: 'CurrentCulture',
                        field: 'CurrentCulture',
                        type: 'InputText',
                        placeholder: 'en-US',
                        advanced: true
                    },
                    {
                        label: 'MaxTradeHoldDuration',
                        field: 'MaxTradeHoldDuration',
                        placeholder: 15,
                        type: 'InputNumber',
                        advanced: true,
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.updates',
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'AutoUpdates',
                        label: 'AutoUpdates',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        field: 'AutoRestart',
                        label: 'AutoRestart',
                        defaultValue: true,
                        advanced: true
                    },
                    {
                        label: 'UpdateChannel',
                        field: 'UpdateChannel',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Stable' },
                            { value: 2, name: 'Experimental' }
                        ],
                        defaultValue: 1,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.remote_access',
                advanced: true,
                fields: [
                    {
                        label: 'IPCHost',
                        field: 'IPCHost',
                        placeholder: '127.0.0.1',
                        type: 'InputText'
                    },
                    {
                        label: 'IPCPort',
                        field: 'IPCPort',
                        placeholder: 1242,
                        type: 'InputNumber',
                        validator: Validators.ushort
                    },
                    {
                        label: 'Headless',
                        field: 'Headless',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    }
                ]
            },
            {
                legend: 'schema.connection',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'SteamProtocols',
                        field: 'SteamProtocols',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'TCP' },
                            { value: 2, name: 'UDP' },
                            { value: 4, name: 'WebSocket' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        label: 'ConnectionTimeout',
                        field: 'ConnectionTimeout',
                        placeholder: 60,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        label: 'FarmingDelay',
                        field: 'FarmingDelay',
                        type: 'InputNumber',
                        placeholder: 15,
                        validator: Validators.byte
                    },
                    {
                        label: 'GiftsLimiterDelay',
                        field: 'GiftsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 1,
                        validator: Validators.byte
                    },
                    {
                        label: 'IdleFarmingPeriod',
                        field: 'IdleFarmingPeriod',
                        type: 'InputNumber',
                        placeholder: 8,
                        validator: Validators.byte
                    },
                    {
                        label: 'InventoryLimiterDelay',
                        field: 'InventoryLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'LoginLimiterDelay',
                        field: 'LoginLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'MaxFarmingTime',
                        field: 'MaxFarmingTime',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'OptimizationMode',
                        field: 'OptimizationMode',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'MaxPerformance' },
                            { value: 1, name: 'MinMemoryUsage' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.advanced',
                advanced: true,
                fields: [
                    {
                        label: 'Debug',
                        field: 'Debug',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'BackgroundGCPeriod',
                        field: 'BackgroundGCPeriod',
                        type: 'InputNumber',
                        placeholder: 0,
                        validator: Validators.byte
                    }
                ]
            }
        ],
        bot: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        type: 'InputText',
                        label: 'Name',
                        field: 'name',
                        required: true,
                        description: 'schema.bot.name.description'
                    },
                    {
                        type: 'InputText',
                        label: 'SteamLogin',
                        field: 'SteamLogin',
                        description: 'schema.bot.SteamLogin.description'
                    },
                    {
                        type: 'InputPassword',
                        label: 'SteamPassword',
                        field: 'SteamPassword',
                        description: 'schema.bot.SteamPassword.description'
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Enabled',
                        field: 'Enabled',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IsBotAccount',
                        field: 'IsBotAccount',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Paused',
                        field: 'Paused',
                        defaultValue: false,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.security',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'PasswordFormat',
                        field: 'PasswordFormat',
                        options: [
                            { value: 0, name: 'PlainText' },
                            { value: 1, name: 'AES' },
                            { value: 2, name: 'ProtectedDataForCurrentUser' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.access',
                advanced: true,
                fields: [
                    {
                        type: 'InputMap',
                        label: 'SteamUserPermissions',
                        field: 'SteamUserPermissions',
                        keyPlaceholder: 'SteamID64',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'FamilySharing' },
                            { value: 2, name: 'Operator' },
                            { value: 3, name: 'Master' }
                        ],
                        defaultValue: 0,
                        keyValidator: Validators.steamid
                    },
                    {
                        type: 'InputText',
                        label: 'SteamParentalPIN',
                        field: 'SteamParentalPIN',
                        placeholder: 0,
                        validator: Validators.parentalPIN
                    }
                ]
            },
            {
                legend: 'schema.community',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'HandleOfflineMessages',
                        field: 'HandleOfflineMessages',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.trading',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamTradeToken',
                        field: 'SteamTradeToken',
                        validator: Validators.tradeToken
                    },
                    {
                        type: 'InputFlag',
                        label: 'TradingPreferences',
                        field: 'TradingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'AcceptDonations' },
                            { value: 2, name: 'SteamTradeMatcher' },
                            { value: 4, name: 'MatchEverything' },
                            { value: 8, name: 'DontAcceptBotTrades' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'LootableTypes',
                        field: 'LootableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputSet',
                        label: 'MatchableTypes',
                        field: 'MatchableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'BoosterPack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'FoilTradingCard' },
                            { value: 4, name: 'ProfileBackground' },
                            { value: 5, name: 'TradingCard' },
                            { value: 6, name: 'SteamGems' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AcceptGifts',
                        field: 'AcceptGifts',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'DismissInventoryNotifications',
                        field: 'DismissInventoryNotifications',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.farming',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'FarmingOrder',
                        field: 'FarmingOrder',
                        options: [
                            { value: 0, name: 'Unordered' },
                            { value: 1, name: 'AppIDsAscending' },
                            { value: 2, name: 'AppIDsDescending' },
                            { value: 3, name: 'CardDropsAscending' },
                            { value: 4, name: 'CardDropsDescending' },
                            { value: 5, name: 'HoursAscending' },
                            { value: 6, name: 'HoursDescending' },
                            { value: 7, name: 'NamesAscending' },
                            { value: 8, name: 'NamesDescending' },
                            { value: 9, name: 'Random' },
                            { value: 10, name: 'BadgeLevelsAscending' },
                            { value: 11, name: 'BadgeLevelsDescending' },
                            { value: 12, name: 'RedeemDateTimesAscending' },
                            { value: 13, name: 'RedeemDateTimesDescending' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputNumber',
                        label: 'SendTradePeriod',
                        field: 'SendTradePeriod',
                        placeholder: 0,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'AutoDiscoveryQueue',
                        field: 'AutoDiscoveryQueue',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'IdleRefundableGames',
                        field: 'IdleRefundableGames',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'FarmOffline',
                        field: 'FarmOffline',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'SendOnFarmingFinished',
                        field: 'SendOnFarmingFinished',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'ShutdownOnFarmingFinished',
                        field: 'ShutdownOnFarmingFinished',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.customization',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'SteamMasterClanID',
                        field: 's_SteamMasterClanID',
                        placeholder: 0,
                        validator: Validators.masterClan
                    },
                    {
                        type: 'InputSet',
                        label: 'GamesPlayedWhileIdle',
                        field: 'GamesPlayedWhileIdle',
                        validator: Validators.uint
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileFarming',
                        field: 'CustomGamePlayedWhileFarming'
                    },
                    {
                        type: 'InputText',
                        label: 'CustomGamePlayedWhileIdle',
                        field: 'CustomGamePlayedWhileIdle'
                    }
                ]
            },
            {
                legend: 'schema.misc',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'RedeemingPreferences',
                        field: 'RedeemingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Forwarding' },
                            { value: 2, name: 'Distributing' },
                            { value: 4, name: 'KeepMissingGames' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'schema.performance',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'CardDropsRestricted',
                        field: 'CardDropsRestricted',
                        defaultValue: true
                    }
                ]
            }
        ]
    }
};
