import Validators from "./validators";

export default {
    latest: {
        asf: [
            {
                legend: 'Basic',
                fields: [
                    {
                        label: 'Owner ID',
                        field: 's_SteamOwnerID',
                        placeholder: '0',
                        type: 'InputText',
                        description: 'Your SteamID64',
                        validator: Validators.steamid
                    }
                ]
            },
            {
                legend: 'Misc',
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
                        label: 'Culture',
                        field: 'CurrentCulture',
                        type: 'InputText',
                        placeholder: 'en-US',
                        advanced: true
                    },
                    {
                        label: 'Max trade hold duration',
                        field: 'MaxTradeHoldDuration',
                        placeholder: 15,
                        type: 'InputNumber',
                        advanced: true,
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'Updates',
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'AutoUpdates',
                        label: 'Automatic updates',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        field: 'AutoRestart',
                        label: 'Automatic restart',
                        defaultValue: true,
                        advanced: true
                    },
                    {
                        label: 'Update channel',
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
                legend: 'Remote access',
                advanced: true,
                fields: [
                    {
                        label: 'IPC host',
                        field: 'IPCHost',
                        placeholder: '127.0.0.1',
                        type: 'InputText'
                    },
                    {
                        label: 'IPC port',
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
                legend: 'Connection',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'Steam protocols',
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
                        label: 'Connection timeout',
                        field: 'ConnectionTimeout',
                        placeholder: 60,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'Performance',
                advanced: true,
                fields: [
                    {
                        label: 'Farming delay',
                        field: 'FarmingDelay',
                        type: 'InputNumber',
                        placeholder: 15,
                        validator: Validators.byte
                    },
                    {
                        label: 'Gifts limiter delay',
                        field: 'GiftsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 1,
                        validator: Validators.byte
                    },
                    {
                        label: 'Idle farming period',
                        field: 'IdleFarmingPeriod',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'Inventory limiter delay',
                        field: 'InventoryLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'Login limiter delay',
                        field: 'LoginLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'Max farming time',
                        field: 'MaxFarmingTime',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'Optimization mode',
                        field: 'OptimizationMode',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'Maximum performance' },
                            { value: 1, name: 'Minimum memory usage' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'Advanced',
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
                legend: 'Basic',
                fields: [
                    {
                        type: 'InputText',
                        label: 'Name',
                        field: 'name',
                        required: true,
                        description: 'Name of the bot/config'
                    },
                    {
                        type: 'InputText',
                        label: 'Steam login',
                        field: 'SteamLogin',
                        description: 'Steam account\'s login'
                    },
                    {
                        type: 'InputText',
                        label: 'Steam password',
                        field: 'SteamPassword',
                        description: 'Steam account\'s password'
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Enabled',
                        field: 'Enabled',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Bot account',
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
                legend: 'Security',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'Password format',
                        field: 'PasswordFormat',
                        options: [
                            { value: 0, name: 'Plain text' },
                            { value: 1, name: 'AES' },
                            { value: 2, name: 'Protected data for current user' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'Access',
                advanced: true,
                fields: [
                    {
                        type: 'InputMap',
                        label: 'User permissions',
                        field: 'SteamUserPermissions',
                        keyPlaceholder: 'SteamID64',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Family sharing' },
                            { value: 2, name: 'Operator' },
                            { value: 3, name: 'Master' }
                        ],
                        defaultValue: 0,
                        keyValidator: Validators.steamid
                    },
                    {
                        type: 'InputText',
                        label: 'Parental PIN',
                        field: 'SteamParentalPIN',
                        placeholder: 0,
                        validator: Validators.parentalPIN
                    }
                ]
            },
            {
                legend: 'Community',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'Handle offline messages',
                        field: 'HandleOfflineMessages',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'Trading',
                fields: [
                    {
                        type: 'InputText',
                        label: 'Trade token',
                        field: 'SteamTradeToken',
                        validator: Validators.tradeToken
                    },
                    {
                        type: 'InputFlag',
                        label: 'Trading preferences',
                        field: 'TradingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Accept donations' },
                            { value: 2, name: 'Steam Trade Matcher' },
                            { value: 4, name: 'Match everything' },
                            { value: 8, name: 'Don\'t accept bot trades' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        type: 'InputSet',
                        label: 'Lootable types',
                        field: 'LootableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'Booster pack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'Foil trading card' },
                            { value: 4, name: 'Profile background' },
                            { value: 5, name: 'Trading card' },
                            { value: 6, name: 'Steam gems' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        type: 'InputSet',
                        label: 'Matchable types',
                        field: 'MatchableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'Booster pack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'Foil trading card' },
                            { value: 4, name: 'Profile background' },
                            { value: 5, name: 'Trading card' },
                            { value: 6, name: 'Steam gems' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Accept gifts',
                        field: 'AcceptGifts',
                        defaultValue: false,
                        advanced: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Dismiss inventory notifications',
                        field: 'DismissInventoryNotifications',
                        defaultValue: false,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'Farming',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'Farming order',
                        field: 'FarmingOrder',
                        options: [
                            { value: 0, name: 'Unordered' },
                            { value: 1, name: 'AppIDs ascending' },
                            { value: 2, name: 'AppIDs descending' },
                            { value: 3, name: 'Card drops ascending' },
                            { value: 4, name: 'Card drops descending' },
                            { value: 5, name: 'Hours ascending' },
                            { value: 6, name: 'Hours descending' },
                            { value: 7, name: 'Names ascending' },
                            { value: 8, name: 'Names descending' },
                            { value: 9, name: 'Random' },
                            { value: 10, name: 'Badge levels ascending' },
                            { value: 11, name: 'Badge levels descending' },
                            { value: 12, name: 'Redeem datetimes ascending' },
                            { value: 13, name: 'Redeem datetimes descending' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputNumber',
                        label: 'Send trade period',
                        field: 'SendTradePeriod',
                        placeholder: 0,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Automatic discovery queue',
                        field: 'AutoDiscoveryQueue',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Idle refundable games',
                        field: 'IdleRefundableGames',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Farm offline',
                        field: 'FarmOffline',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Send on farming finished',
                        field: 'SendOnFarmingFinished',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Shutdown on farming finished',
                        field: 'ShutdownOnFarmingFinished',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'Customization',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'Steam master clan ID',
                        field: 's_SteamMasterClanID',
                        placeholder: 0,
                        validator: Validators.masterClan
                    },
                    {
                        type: 'InputSet',
                        label: 'Games played while idle',
                        field: 'GamesPlayedWhileIdle',
                        validator: Validators.uint
                    },
                    {
                        type: 'InputText',
                        label: 'Custom game played while farming',
                        field: 'CustomGamePlayedWhileFarming'
                    },
                    {
                        type: 'InputText',
                        label: 'Custom game played while idle',
                        field: 'CustomGamePlayedWhileIdle'
                    }
                ]
            },
            {
                legend: 'Misc',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'Redeeming preferences',
                        field: 'RedeemingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Forwarding' },
                            { value: 2, name: 'Distributing' },
                            { value: 4, name: 'Keep missing games' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'Performance',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'Card drops restricted',
                        field: 'CardDropsRestricted',
                        defaultValue: true
                    }
                ]
            }
        ]
    },
    'V3.0.1.2': {
        asf: [
            {
                legend: 'Basic',
                fields: [
                    {
                        label: 'Owner ID',
                        field: 's_SteamOwnerID',
                        placeholder: '0',
                        type: 'InputText',
                        description: 'Your SteamID64',
                        validator: Validators.steamid
                    }
                ]
            },
            {
                legend: 'Misc',
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
                        label: 'Culture',
                        field: 'CurrentCulture',
                        type: 'InputText',
                        placeholder: 'en-US',
                        advanced: true
                    },
                    {
                        label: 'Max trade hold duration',
                        field: 'MaxTradeHoldDuration',
                        placeholder: 15,
                        type: 'InputNumber',
                        advanced: true,
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'Updates',
                fields: [
                    {
                        type: 'InputCheckbox',
                        field: 'AutoUpdates',
                        label: 'Automatic updates',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        field: 'AutoRestart',
                        label: 'Automatic restart',
                        defaultValue: true,
                        advanced: true
                    },
                    {
                        label: 'Update channel',
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
                legend: 'Remote access',
                advanced: true,
                fields: [
                    {
                        label: 'IPC host',
                        field: 'IPCHost',
                        placeholder: '127.0.0.1',
                        type: 'InputText'
                    },
                    {
                        label: 'IPC port',
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
                legend: 'Connection',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'Steam protocols',
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
                        label: 'Connection timeout',
                        field: 'ConnectionTimeout',
                        placeholder: 60,
                        type: 'InputNumber',
                        validator: Validators.byte
                    }
                ]
            },
            {
                legend: 'Performance',
                advanced: true,
                fields: [
                    {
                        label: 'Farming delay',
                        field: 'FarmingDelay',
                        type: 'InputNumber',
                        placeholder: 15,
                        validator: Validators.byte
                    },
                    {
                        label: 'Gifts limiter delay',
                        field: 'GiftsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 1,
                        validator: Validators.byte
                    },
                    {
                        label: 'Idle farming period',
                        field: 'IdleFarmingPeriod',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'Inventory limiter delay',
                        field: 'InventoryLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'Login limiter delay',
                        field: 'LoginLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'Max farming time',
                        field: 'MaxFarmingTime',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'Optimization mode',
                        field: 'OptimizationMode',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'Maximum performance' },
                            { value: 1, name: 'Minimum memory usage' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'Advanced',
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
                legend: 'Basic',
                fields: [
                    {
                        type: 'InputText',
                        label: 'Name',
                        field: 'name',
                        required: true,
                        description: 'Name of the bot/config'
                    },
                    {
                        type: 'InputText',
                        label: 'Steam login',
                        field: 'SteamLogin',
                        description: 'Steam account\'s login'
                    },
                    {
                        type: 'InputText',
                        label: 'Steam password',
                        field: 'SteamPassword',
                        description: 'Steam account\'s password'
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Enabled',
                        field: 'Enabled',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Bot account',
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
                legend: 'Security',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'Password format',
                        field: 'PasswordFormat',
                        options: [
                            { value: 0, name: 'Plain text' },
                            { value: 1, name: 'AES' },
                            { value: 2, name: 'Protected data for current user' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'Access',
                advanced: true,
                fields: [
                    {
                        type: 'InputMap',
                        label: 'User permissions',
                        field: 'SteamUserPermissions',
                        keyPlaceholder: 'SteamID64',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Family sharing' },
                            { value: 2, name: 'Operator' },
                            { value: 3, name: 'Master' }
                        ],
                        defaultValue: 0,
                        keyValidator: Validators.steamid
                    },
                    {
                        type: 'InputText',
                        label: 'Parental PIN',
                        field: 'SteamParentalPIN',
                        placeholder: 0,
                        validator: Validators.parentalPIN
                    }
                ]
            },
            {
                legend: 'Community',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'Handle offline messages',
                        field: 'HandleOfflineMessages',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'Trading',
                fields: [
                    {
                        type: 'InputText',
                        label: 'Trade token',
                        field: 'SteamTradeToken',
                        validator: Validators.tradeToken
                    },
                    {
                        type: 'InputFlag',
                        label: 'Trading preferences',
                        field: 'TradingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Accept donations' },
                            { value: 2, name: 'Steam Trade Matcher' },
                            { value: 4, name: 'Match everything' },
                            { value: 8, name: 'Don\'t accept bot trades' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        type: 'InputSet',
                        label: 'Lootable types',
                        field: 'LootableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'Booster pack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'Foil trading card' },
                            { value: 4, name: 'Profile background' },
                            { value: 5, name: 'Trading card' },
                            { value: 6, name: 'Steam gems' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        type: 'InputSet',
                        label: 'Matchable types',
                        field: 'MatchableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'Booster pack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'Foil trading card' },
                            { value: 4, name: 'Profile background' },
                            { value: 5, name: 'Trading card' },
                            { value: 6, name: 'Steam gems' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Accept gifts',
                        field: 'AcceptGifts',
                        defaultValue: false,
                        advanced: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Dismiss inventory notifications',
                        field: 'DismissInventoryNotifications',
                        defaultValue: false,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'Farming',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'Farming order',
                        field: 'FarmingOrder',
                        options: [
                            { value: 0, name: 'Unordered' },
                            { value: 1, name: 'AppIDs ascending' },
                            { value: 2, name: 'AppIDs descending' },
                            { value: 3, name: 'Card drops ascending' },
                            { value: 4, name: 'Card drops descending' },
                            { value: 5, name: 'Hours ascending' },
                            { value: 6, name: 'Hours descending' },
                            { value: 7, name: 'Names ascending' },
                            { value: 8, name: 'Names descending' },
                            { value: 9, name: 'Random' },
                            { value: 10, name: 'Badge levels ascending' },
                            { value: 11, name: 'Badge levels descending' },
                            { value: 12, name: 'Redeem datetimes ascending' },
                            { value: 13, name: 'Redeem datetimes descending' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputNumber',
                        label: 'Send trade period',
                        field: 'SendTradePeriod',
                        placeholder: 0,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Automatic discovery queue',
                        field: 'AutoDiscoveryQueue',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Idle refundable games',
                        field: 'IdleRefundableGames',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Farm offline',
                        field: 'FarmOffline',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Send on farming finished',
                        field: 'SendOnFarmingFinished',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Shutdown on farming finished',
                        field: 'ShutdownOnFarmingFinished',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'Customization',
                advanced: true,
                fields: [
                    {
                        type: 'InputText',
                        label: 'Steam master clan ID',
                        field: 's_SteamMasterClanID',
                        placeholder: 0,
                        validator: Validators.masterClan
                    },
                    {
                        type: 'InputSet',
                        label: 'Games played while idle',
                        field: 'GamesPlayedWhileIdle',
                        validator: Validators.uint
                    },
                    {
                        type: 'InputText',
                        label: 'Custom game played while farming',
                        field: 'CustomGamePlayedWhileFarming'
                    },
                    {
                        type: 'InputText',
                        label: 'Custom game played while idle',
                        field: 'CustomGamePlayedWhileIdle'
                    }
                ]
            },
            {
                legend: 'Misc',
                advanced: true,
                fields: [
                    {
                        type: 'InputFlag',
                        label: 'Redeeming preferences',
                        field: 'RedeemingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Forwarding' },
                            { value: 2, name: 'Distributing' },
                            { value: 4, name: 'Keep missing games' }
                        ],
                        defaultValue: 0
                    }
                ]
            },
            {
                legend: 'Performance',
                advanced: true,
                fields: [
                    {
                        type: 'InputCheckbox',
                        label: 'Card drops restricted',
                        field: 'CardDropsRestricted',
                        defaultValue: true
                    }
                ]
            }
        ]
    }
}