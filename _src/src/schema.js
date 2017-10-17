import Validators from "./validators";

export default {
    Latest: {
        asf: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        label: 'schema.basic.owner.label',
                        field: 's_SteamOwnerID',
                        placeholder: '0',
                        type: 'InputText',
                        description: 'schema.basic.owner.description',
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
                        label: 'schema.misc.statistics',
                        defaultValue: true,
                        advanced: true
                    },
                    {
                        label: 'schema.misc.blacklist',
                        field: 'Blacklist',
                        type: 'InputSet',
                        validator: Validators.uint
                    },
                    {
                        label: 'schema.misc.culture.label',
                        field: 'CurrentCulture',
                        type: 'InputText',
                        placeholder: 'en-US',
                        advanced: true
                    },
                    {
                        label: 'schema.misc.max_trade_hold',
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
                        label: 'schema.updates.auto_update',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        field: 'AutoRestart',
                        label: 'schema.updates.auto_restart',
                        defaultValue: true,
                        advanced: true
                    },
                    {
                        label: 'schema.updates.channel',
                        field: 'UpdateChannel',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'schema.updates.channel.none' },
                            { value: 1, name: 'schema.updates.channel.stable' },
                            { value: 2, name: 'schema.updates.channel.experimental' }
                        ],
                        defaultValue: 1,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.access',
                advanced: true,
                fields: [
                    {
                        label: 'schema.access.ipc_host.label',
                        field: 'IPCHost',
                        placeholder: '127.0.0.1',
                        type: 'InputText'
                    },
                    {
                        label: 'schema.access.ipc_port',
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
                        label: 'schema.connection.protocols',
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
                        label: 'schema.connection.timeout',
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
                        label: 'schema.performance.farm_delay',
                        field: 'FarmingDelay',
                        type: 'InputNumber',
                        placeholder: 15,
                        validator: Validators.byte
                    },
                    {
                        label: 'schema.performance.gift_delay',
                        field: 'GiftsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 1,
                        validator: Validators.byte
                    },
                    {
                        label: 'schema.performance.idle_period',
                        field: 'IdleFarmingPeriod',
                        type: 'InputNumber',
                        placeholder: 8,
                        validator: Validators.byte
                    },
                    {
                        label: 'schema.performance.inventory_delay',
                        field: 'InventoryLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'schema.performance.login_delay',
                        field: 'LoginLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'schema.performance.max_farm_time',
                        field: 'MaxFarmingTime',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'schema.performance.optimization',
                        field: 'OptimizationMode',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'schema.performance.optimization.max_performance' },
                            { value: 1, name: 'schema.performance.optimization.min_usage' }
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
                        label: 'schema.advanced.debug',
                        field: 'Debug',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'schema.advanced.background_gc',
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
                        label: 'schema.bot.name',
                        field: 'name',
                        required: true,
                        description: 'schema.bot.name.description'
                    },
                    {
                        type: 'InputText',
                        label: 'schema.bot.login',
                        field: 'SteamLogin',
                        description: 'schema.bot.login.description'
                    },
                    {
                        type: 'InputPassword',
                        label: 'schema.bot.password',
                        field: 'SteamPassword',
                        description: 'schema.bot.password.description'
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.bot.enabled',
                        field: 'Enabled',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.bot.bot_account',
                        field: 'IsBotAccount',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.bot.paused',
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
                        label: 'schema.security.password_format',
                        field: 'PasswordFormat',
                        options: [
                            { value: 0, name: 'schema.security.password_format.plain' },
                            { value: 1, name: 'schema.security.password_format.aes' },
                            { value: 2, name: 'schema.security.password_format.protect' }
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
                        label: 'schema.access.user_permissions',
                        field: 'SteamUserPermissions',
                        keyPlaceholder: 'SteamID64',
                        values: [
                            { value: 0, name: 'schema.access.user_permissions.none' },
                            { value: 1, name: 'schema.access.user_permissions.family' },
                            { value: 2, name: 'schema.access.user_permissions.operator' },
                            { value: 3, name: 'schema.access.user_permissions.master' }
                        ],
                        defaultValue: 0,
                        keyValidator: Validators.steamid
                    },
                    {
                        type: 'InputText',
                        label: 'schema.access.pin',
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
                        label: 'schema.community.offine_messages',
                        field: 'HandleOfflineMessages',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.trading',
                fields: [
                    {
                        type: 'InputText',
                        label: 'schema.trading.token',
                        field: 'SteamTradeToken',
                        validator: Validators.tradeToken
                    },
                    {
                        type: 'InputFlag',
                        label: 'schema.trading.preferences',
                        field: 'TradingPreferences',
                        values: [
                            { value: 0, name: 'schema.trading.token.none' },
                            { value: 1, name: 'schema.trading.token.donations' },
                            { value: 2, name: 'schema.trading.token.matcher' },
                            { value: 4, name: 'schema.trading.token.everything' },
                            { value: 8, name: 'schema.trading.token.not_accept' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        type: 'InputSet',
                        label: 'schema.trading.lootable',
                        field: 'LootableTypes',
                        values: [
                            { value: 0, name: 'schema.trading.type.unknown' },
                            { value: 1, name: 'schema.trading.type.booster_pack' },
                            { value: 2, name: 'schema.trading.type.emoticon' },
                            { value: 3, name: 'schema.trading.type.foil_trading_card' },
                            { value: 4, name: 'schema.trading.type.profile_background' },
                            { value: 5, name: 'schema.trading.type.trading_card' },
                            { value: 6, name: 'schema.trading.type.steam_gems' },
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        type: 'InputSet',
                        label: 'schema.trading.matchable',
                        field: 'MatchableTypes',
                        values: [
                            { value: 0, name: 'schema.trading.type.unknown' },
                            { value: 1, name: 'schema.trading.type.booster_pack' },
                            { value: 2, name: 'schema.trading.type.emoticon' },
                            { value: 3, name: 'schema.trading.type.foil_trading_card' },
                            { value: 4, name: 'schema.trading.type.profile_background' },
                            { value: 5, name: 'schema.trading.type.trading_card' },
                            { value: 6, name: 'schema.trading.type.steam_gems' },
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.trading.accept_gifts',
                        field: 'AcceptGifts',
                        defaultValue: false,
                        advanced: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.trading.dismission_notification',
                        field: 'DismissInventoryNotifications',
                        defaultValue: false,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.farming',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'schema.farming.order',
                        field: 'FarmingOrder',
                        options: [
                            { value: 0, name: 'schema.farming.order.type.unordered' },
                            { value: 1, name: 'schema.farming.order.type.appids_ascending' },
                            { value: 2, name: 'schema.farming.order.type.appids_descending' },
                            { value: 3, name: 'schema.farming.order.type.card_drops_ascending' },
                            { value: 4, name: 'schema.farming.order.type.card_drops_descending' },
                            { value: 5, name: 'schema.farming.order.type.hours_ascending' },
                            { value: 6, name: 'schema.farming.order.type.hours_descending' },
                            { value: 7, name: 'schema.farming.order.type.names_ascending' },
                            { value: 8, name: 'schema.farming.order.type.names_descending' },
                            { value: 9, name: 'schema.farming.order.type.random' },
                            { value: 10, name: 'schema.farming.order.type.badge_levels_ascending' },
                            { value: 11, name: 'schema.farming.order.type.badge_levels_descending' },
                            { value: 12, name: 'schema.farming.order.type.redeem_datetimes_ascending' },
                            { value: 13, name: 'schema.farming.order.type.redeem_datetimes_descending' },
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputNumber',
                        label: 'schema.farming.period',
                        field: 'SendTradePeriod',
                        placeholder: 0,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.farming.auto_queue',
                        field: 'AutoDiscoveryQueue',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.farming.idle_refundable',
                        field: 'IdleRefundableGames',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.farming.offline',
                        field: 'FarmOffline',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.farming.send_on_finish',
                        field: 'SendOnFarmingFinished',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.farming.shutdown_on_finish',
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
                        label: 'schema.customization.master_id',
                        field: 's_SteamMasterClanID',
                        placeholder: 0,
                        validator: Validators.masterClan
                    },
                    {
                        type: 'InputSet',
                        label: 'schema.customization.play_while_idle',
                        field: 'GamesPlayedWhileIdle',
                        validator: Validators.uint
                    },
                    {
                        type: 'InputText',
                        label: 'schema.customization.game_play_while_farm',
                        field: 'CustomGamePlayedWhileFarming'
                    },
                    {
                        type: 'InputText',
                        label: 'schema.customization.game_play_while_idle',
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
                        label: 'schema.misc.redeeming',
                        field: 'RedeemingPreferences',
                        values: [
                            { value: 0, name: 'schema.misc.redeeming.type.none' },
                            { value: 1, name: 'schema.misc.redeeming.type.forwarding' },
                            { value: 2, name: 'schema.misc.redeeming.type.distributing' },
                            { value: 4, name: 'schema.misc.redeeming.type.keep_missing_games' },
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
                        label: 'schema.performance.restricted',
                        field: 'CardDropsRestricted',
                        defaultValue: true
                    }
                ]
            }
        ]
    },
    'V3.0.1.6+': {
        asf: [
            {
                legend: 'schema.basic',
                fields: [
                    {
                        label: 'schema.basic.owner.label',
                        field: 's_SteamOwnerID',
                        placeholder: '0',
                        type: 'InputText',
                        description: 'schema.basic.owner.description',
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
                        label: 'schema.misc.statistics',
                        defaultValue: true,
                        advanced: true
                    },
                    {
                        label: 'schema.misc.blacklist',
                        field: 'Blacklist',
                        type: 'InputSet',
                        validator: Validators.uint
                    },
                    {
                        label: 'schema.misc.culture.label',
                        field: 'CurrentCulture',
                        type: 'InputText',
                        placeholder: 'en-US',
                        advanced: true
                    },
                    {
                        label: 'schema.misc.max_trade_hold',
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
                        label: 'schema.updates.auto_update',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        field: 'AutoRestart',
                        label: 'schema.updates.auto_restart',
                        defaultValue: true,
                        advanced: true
                    },
                    {
                        label: 'schema.updates.channel',
                        field: 'UpdateChannel',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'schema.updates.channel.none' },
                            { value: 1, name: 'schema.updates.channel.stable' },
                            { value: 2, name: 'schema.updates.channel.experimental' }
                        ],
                        defaultValue: 1,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.access',
                advanced: true,
                fields: [
                    {
                        label: 'schema.access.ipc_host.label',
                        field: 'IPCHost',
                        placeholder: '127.0.0.1',
                        type: 'InputText'
                    },
                    {
                        label: 'schema.access.ipc_port',
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
                        label: 'schema.connection.protocols',
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
                        label: 'schema.connection.timeout',
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
                        label: 'schema.performance.farm_delay',
                        field: 'FarmingDelay',
                        type: 'InputNumber',
                        placeholder: 15,
                        validator: Validators.byte
                    },
                    {
                        label: 'schema.performance.gift_delay',
                        field: 'GiftsLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 1,
                        validator: Validators.byte
                    },
                    {
                        label: 'schema.performance.idle_period',
                        field: 'IdleFarmingPeriod',
                        type: 'InputNumber',
                        placeholder: 8,
                        validator: Validators.byte
                    },
                    {
                        label: 'schema.performance.inventory_delay',
                        field: 'InventoryLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 3,
                        validator: Validators.byte
                    },
                    {
                        label: 'schema.performance.login_delay',
                        field: 'LoginLimiterDelay',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'schema.performance.max_farm_time',
                        field: 'MaxFarmingTime',
                        type: 'InputNumber',
                        placeholder: 10,
                        validator: Validators.byte
                    },
                    {
                        label: 'schema.performance.optimization',
                        field: 'OptimizationMode',
                        type: 'InputSelect',
                        options: [
                            { value: 0, name: 'schema.performance.optimization.max_performance' },
                            { value: 1, name: 'schema.performance.optimization.min_usage' }
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
                        label: 'schema.advanced.debug',
                        field: 'Debug',
                        defaultValue: false,
                        type: 'InputCheckbox'
                    },
                    {
                        label: 'schema.advanced.background_gc',
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
                        label: 'schema.bot.name',
                        field: 'name',
                        required: true,
                        description: 'schema.bot.name.description'
                    },
                    {
                        type: 'InputText',
                        label: 'schema.bot.login',
                        field: 'SteamLogin',
                        description: 'schema.bot.login.description'
                    },
                    {
                        type: 'InputPassword',
                        label: 'schema.bot.password',
                        field: 'SteamPassword',
                        description: 'schema.bot.password.description'
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.bot.enabled',
                        field: 'Enabled',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.bot.bot_account',
                        field: 'IsBotAccount',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.bot.paused',
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
                        label: 'schema.security.password_format',
                        field: 'PasswordFormat',
                        options: [
                            { value: 0, name: 'schema.security.password_format.plain' },
                            { value: 1, name: 'schema.security.password_format.aes' },
                            { value: 2, name: 'schema.security.password_format.protect' }
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
                        label: 'schema.access.user_permissions',
                        field: 'SteamUserPermissions',
                        keyPlaceholder: 'SteamID64',
                        values: [
                            { value: 0, name: 'schema.access.user_permissions.none' },
                            { value: 1, name: 'schema.access.user_permissions.family' },
                            { value: 2, name: 'schema.access.user_permissions.operator' },
                            { value: 3, name: 'schema.access.user_permissions.master' }
                        ],
                        defaultValue: 0,
                        keyValidator: Validators.steamid
                    },
                    {
                        type: 'InputText',
                        label: 'schema.access.pin',
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
                        label: 'schema.community.offine_messages',
                        field: 'HandleOfflineMessages',
                        defaultValue: false
                    }
                ]
            },
            {
                legend: 'schema.trading',
                fields: [
                    {
                        type: 'InputText',
                        label: 'schema.trading.token',
                        field: 'SteamTradeToken',
                        validator: Validators.tradeToken
                    },
                    {
                        type: 'InputFlag',
                        label: 'schema.trading.preferences',
                        field: 'TradingPreferences',
                        values: [
                            { value: 0, name: 'schema.trading.token.none' },
                            { value: 1, name: 'schema.trading.token.donations' },
                            { value: 2, name: 'schema.trading.token.matcher' },
                            { value: 4, name: 'schema.trading.token.everything' },
                            { value: 8, name: 'schema.trading.token.not_accept' }
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        type: 'InputSet',
                        label: 'schema.trading.lootable',
                        field: 'LootableTypes',
                        values: [
                            { value: 0, name: 'schema.trading.type.unknown' },
                            { value: 1, name: 'schema.trading.type.booster_pack' },
                            { value: 2, name: 'schema.trading.type.emoticon' },
                            { value: 3, name: 'schema.trading.type.foil_trading_card' },
                            { value: 4, name: 'schema.trading.type.profile_background' },
                            { value: 5, name: 'schema.trading.type.trading_card' },
                            { value: 6, name: 'schema.trading.type.steam_gems' },
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        type: 'InputSet',
                        label: 'schema.trading.matchable',
                        field: 'MatchableTypes',
                        values: [
                            { value: 0, name: 'schema.trading.type.unknown' },
                            { value: 1, name: 'schema.trading.type.booster_pack' },
                            { value: 2, name: 'schema.trading.type.emoticon' },
                            { value: 3, name: 'schema.trading.type.foil_trading_card' },
                            { value: 4, name: 'schema.trading.type.profile_background' },
                            { value: 5, name: 'schema.trading.type.trading_card' },
                            { value: 6, name: 'schema.trading.type.steam_gems' },
                        ],
                        defaultValue: 0,
                        advanced: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.trading.accept_gifts',
                        field: 'AcceptGifts',
                        defaultValue: false,
                        advanced: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.trading.dismission_notification',
                        field: 'DismissInventoryNotifications',
                        defaultValue: false,
                        advanced: true
                    }
                ]
            },
            {
                legend: 'schema.farming',
                advanced: true,
                fields: [
                    {
                        type: 'InputSelect',
                        label: 'schema.farming.order',
                        field: 'FarmingOrder',
                        options: [
                            { value: 0, name: 'schema.farming.order.type.unordered' },
                            { value: 1, name: 'schema.farming.order.type.appids_ascending' },
                            { value: 2, name: 'schema.farming.order.type.appids_descending' },
                            { value: 3, name: 'schema.farming.order.type.card_drops_ascending' },
                            { value: 4, name: 'schema.farming.order.type.card_drops_descending' },
                            { value: 5, name: 'schema.farming.order.type.hours_ascending' },
                            { value: 6, name: 'schema.farming.order.type.hours_descending' },
                            { value: 7, name: 'schema.farming.order.type.names_ascending' },
                            { value: 8, name: 'schema.farming.order.type.names_descending' },
                            { value: 9, name: 'schema.farming.order.type.random' },
                            { value: 10, name: 'schema.farming.order.type.badge_levels_ascending' },
                            { value: 11, name: 'schema.farming.order.type.badge_levels_descending' },
                            { value: 12, name: 'schema.farming.order.type.redeem_datetimes_ascending' },
                            { value: 13, name: 'schema.farming.order.type.redeem_datetimes_descending' },
                        ],
                        defaultValue: 0
                    },
                    {
                        type: 'InputNumber',
                        label: 'schema.farming.period',
                        field: 'SendTradePeriod',
                        placeholder: 0,
                        validator: Validators.byte
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.farming.auto_queue',
                        field: 'AutoDiscoveryQueue',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.farming.idle_refundable',
                        field: 'IdleRefundableGames',
                        defaultValue: true
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.farming.offline',
                        field: 'FarmOffline',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.farming.send_on_finish',
                        field: 'SendOnFarmingFinished',
                        defaultValue: false
                    },
                    {
                        type: 'InputCheckbox',
                        label: 'schema.farming.shutdown_on_finish',
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
                        label: 'schema.customization.master_id',
                        field: 's_SteamMasterClanID',
                        placeholder: 0,
                        validator: Validators.masterClan
                    },
                    {
                        type: 'InputSet',
                        label: 'schema.customization.play_while_idle',
                        field: 'GamesPlayedWhileIdle',
                        validator: Validators.uint
                    },
                    {
                        type: 'InputText',
                        label: 'schema.customization.game_play_while_farm',
                        field: 'CustomGamePlayedWhileFarming'
                    },
                    {
                        type: 'InputText',
                        label: 'schema.customization.game_play_while_idle',
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
                        label: 'schema.misc.redeeming',
                        field: 'RedeemingPreferences',
                        values: [
                            { value: 0, name: 'schema.misc.redeeming.type.none' },
                            { value: 1, name: 'schema.misc.redeeming.type.forwarding' },
                            { value: 2, name: 'schema.misc.redeeming.type.distributing' },
                            { value: 4, name: 'schema.misc.redeeming.type.keep_missing_games' },
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
                        label: 'schema.performance.restricted',
                        field: 'CardDropsRestricted',
                        defaultValue: true
                    }
                ]
            }
        ]
    }
}
