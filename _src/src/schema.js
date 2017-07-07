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
                        description: 'Your steamid64',
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
                        label: 'Statisticst',
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
                        placeholder: 'pl-PL',
                        advanced: true
                    },
                    {
                        label: 'Max Trade Hold Duration',
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
                        label: 'Auto Updates',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        field: 'AutoRestart',
                        label: 'Auto Restart',
                        defaultValue: true,
                        advanced: true
                    },
                    {
                        label: 'Update Channel',
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
                legend: 'Remote Access',
                advanced: true,
                fields: [
                    {
                        label: 'IPC Host',
                        field: 'IPCHost',
                        placeholder: '127.0.0.1',
                        type: 'InputText'
                    },
                    {
                        label: 'IPC Port',
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
                        label: 'Steam Protocol',
                        field: 'SteamProtocol',
                        options: [
                            { value: 0, name: 'TCP' },
                            { value: 1, name: 'UDP' }
                        ],
                        defaultValue: 0,
                        type: 'InputSelect'
                    },
                    {
                        label: 'Connection Timeout',
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
                        label: 'Farming Delay',
                        field: 'FarmingDelay',
                        type: 'InputNumber',
                        placeholder: 15,
                        validator: Validators.byte
                    },
                    {
                        label: 'Gifts Limiter Delay',
                        field: 'GiftsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 1,
                        validator: Validators.byte
                    },
                    {
                        label: 'Idle Farming Period',
                        field: 'IdleFarmingPeriod',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'Inventory Limiter Delay',
                        field: 'InventoryLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'Login Limiter Delay',
                        field: 'LoginLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'Max Farming Time',
                        field: 'MaxFarmingTime',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'Optimization Mode',
                        field: 'OptimizationMode',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'Maximum Performance' },
                            { value: 1, name: 'Minimum Memory Usage' }
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
                        description: 'Name of the config'
                    },
                    {
                        type: 'InputText',
                        label: 'Steam Login',
                        field: 'SteamLogin',
                        description: 'Account\'s steam login'
                    },
                    {
                        type: 'InputText',
                        label: 'Steam Password',
                        field: 'SteamPassword',
                        description: 'Account\'s steam password'
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Enabled',
                        field: 'Enabled',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Bot Account',
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
                        label: 'Password Format',
                        field: 'PasswordFormat',
                        options: [
                            { value: 0, name: 'Plain Text' },
                            { value: 1, name: 'AES' },
                            { value: 2, name: 'Protected Data For Current User' }
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
                        label: 'User Permissions',
                        field: 'SteamUserPermissions',
                        keyPlaceholder: 'Steamid',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Family Sharing' },
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
                        label: 'Handle Offline Messages',
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
                        label: 'Trading Preferences',
                        field: 'TradingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Accept Donations' },
                            { value: 2, name: 'Steam Trade Matcher' },
                            { value: 4, name: 'Match Everything' },
                            { value: 8, name: 'Don\'t accept Bot Trades' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        type: 'InputSet',
                        label: 'Lootable Types',
                        field: 'LootableTypes',
                        values: [
                            { value: 0, name: 'Unknown' },
                            { value: 1, name: 'Booster Pack' },
                            { value: 2, name: 'Emoticon' },
                            { value: 3, name: 'Foil Trading Card' },
                            { value: 4, name: 'Profile Background' },
                            { value: 5, name: 'Trading Card' },
                            { value: 6, name: 'Steam Gems' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Accept Gifts',
                        field: 'AcceptGifts',
                        defaultValue: false,
                        advanced: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Dismiss Inventory Notifications',
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
                        label: 'Farming Order',
                        field: 'FarmingOrder',
                        options: [
                            { value: 0, name: 'Unordered' },
                            { value: 1, name: 'AppIDs Ascending' },
                            { value: 2, name: 'AppIDs Descending' },
                            { value: 3, name: 'Card Drops Ascending' },
                            { value: 4, name: 'Card Drops Descending' },
                            { value: 5, name: 'Hours Ascending' },
                            { value: 6, name: 'Hours Descending' },
                            { value: 7, name: 'Names Ascending' },
                            { value: 8, name: 'Names Descending' },
                            { value: 9, name: 'Random' }
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputNumber',
                        label: 'Send Trade Period',
                        field: 'SendTradePeriod',
                        placeholder: 0,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Farm Offline',
                        field: 'FarmOffline',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Send On Farming Finished',
                        field: 'SendOnFarmingFinished',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'Shutdown On Farming Finished',
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
                        label: 'Master Clan ID',
                        field: 's_SteamMasterClanID',
                        placeholder: 0,
                        validator: Validators.masterClan
                    },
                    {
                        type: 'InputSet',
                        label: 'Games Played While Idle',
                        field: 'GamesPlayedWhileIdle',
                        validator: Validators.uint
                    },
                    {
                        type: 'InputText',
                        label: 'Custom Game Played While Farming',
                        field: 'CustomGamePlayedWhileFarming'
                    },
                    {
                        type: 'InputText',
                        label: 'Custom Game Played While Idle',
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
                        label: 'Redeeming Preferences',
                        field: 'RedeemingPreferences',
                        values: [
                            { value: 0, name: 'None' },
                            { value: 1, name: 'Forwarding' },
                            { value: 2, name: 'Distributing' },
                            { value: 4, name: 'Keep Missing Games' }
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
                        label: 'Card Drops Restricted',
                        field: 'CardDropsRestricted',
                        defaultValue: true
                    }
                ]
            }
        ]
    }
}