//#region Setup
const tmpIPCPassword = get('IPCPassword');

if (tmpIPCPassword) {
    $.ajaxSetup({
        beforeSend: function (jqXHR) {
            jqXHR.setRequestHeader('Authentication', tmpIPCPassword);
        }
    });
}

$.ajaxSetup({
    statusCode: {
        401: function () {
            store('IPCPassword', '');
            store('IsAuthorized', false);
            window.location.replace('../index.html');
        },
        403: function () {
            store('IPCPassword', '');
            store('IsAuthorized', false);
            window.location.replace('../index.html');
        }
    }
});
//#endregion Setup

//#region Footer
$.ajax({
    url: '/Api/ASF',
    type: 'GET',
    success: function (data) {
        var ver = data['Result'].Version,
            verNr = ver.Major + '.' + ver.Minor + '.' + ver.Build + '.' + ver.Revision;
            
        $('#version').text(verNr);
        $('#changelog').attr('href', 'https://github.com/JustArchi/ArchiSteamFarm/releases/tag/' + verNr);
    }
});
//#endregion Footer

//#region Bot Status Buttons
function displayBotStatus() {
    var offline = 0,
        online = 0,
        farming = 0;

    $.ajax({
        url: '/Api/Bot/ASF',
        type: 'GET',
        success: function (data) {
            var json = data['Result'];

            for (var i = 0; i < json.length; i++) {
                var obj = json[i],
                    KeepRunning = obj.KeepRunning,
                    TimeRemaining = obj.CardsFarmer.TimeRemaining;

                if (KeepRunning === false) {
                    offline++;
                } else {
                    if (TimeRemaining === '00:00:00') {
                        online++;
                    } else {
                        farming++;
                    }
                }
            }

            $('#offlineBots').text(offline);
            $('#onlineBots').text(online);
            $('#farmingBots').text(farming);
        }
    });
}

displayBotStatus();
window.setInterval(function () { displayBotStatus(); }, 5000);
//#endregion Bot Status Buttons

//#region ASF Information
function displayRAMUsage() {
    $.ajax({
        url: '/Api/ASF',
        type: 'GET',
        success: function (data) {
            var mem = data['Result'].MemoryUsage,
                memMB = (mem / 1024).toFixed(2);

            $('#ramUsage').html(memMB + ' MB');
        }
    });
}

displayRAMUsage();
window.setInterval(function () { displayRAMUsage(); }, 10000);

function displayUptime() {
    $.ajax({
        url: '/Api/ASF',
        type: 'GET',
        success: function (data) {
            var pst = data['Result'].ProcessStartTime,
                start = new Date(pst),
                now = new Date(),
                diff = now.getTime() - start.getTime();

            var d = Math.floor(diff / (1000 * 60 * 60 * 24));
            diff -= d * (1000 * 60 * 60 * 24);

            var h = Math.floor(diff / (1000 * 60 * 60));
            diff -= h * (1000 * 60 * 60);

            var m = Math.floor(diff / (1000 * 60));

            h = (h < 10 ? '0' : '') + h;
            m = (m < 10 ? '0' : '') + m;

            up = d + 'd ' + h + 'h ' + m + 'm';

            $('#uptime').html(up);
        }
    });
}

displayUptime();
window.setInterval(function () { displayUptime(); }, 60000);
//#endregion ASF Information

//#region Commands Page
var $cmdInput = $('#commandInput');
function fillCommand(cmd) { $cmdInput.val(cmd + ' '); }
function fillBots(bot) { $cmdInput.val($cmdInput.val() + bot); }

function getDateAndTime() {
    var date = new Date();
    return ('0' + date.getDate()).slice(-2) + '.'
        + ('0' + (date.getMonth() + 1)).slice(-2) + '.'
        + date.getFullYear() + ' @ '
        + ('0' + date.getHours()).slice(-2) + ':'
        + ('0' + date.getMinutes()).slice(-2) + ':'
        + ('0' + date.getSeconds()).slice(-2);
}

function logCommand(state, cmd) {
    var tmpAutoClear = get('autoClear');

    if (state) {
        $('#commandSent').val($.i18n('commands-sent', getDateAndTime(), cmd));
        return;
    } 

    var response = $.i18n('commands-response', getDateAndTime(), cmd);
	
    if (tmpAutoClear === 'false') {
		var oldText = $('.box-content-commands').text();
		$('.box-content-commands').text(oldText + '\n' + response + '\n');
    } else {
        $('.box-content-commands').text(response);
    }
}

function sendCommand() {
    var command = $cmdInput.val(),
        requestURL = '/Api/Command/' + encodeURIComponent(command), 
        tmpAutoClear = get('autoClear');

    if (command === '') return;

    logCommand(true, command);

    var response = $.i18n('commands-waiting', getDateAndTime());

    if (tmpAutoClear === 'false') {
        if ($('.box-content-commands').text() === '') {
            $('.box-content-commands').append(response + '\n');
        } else {
            $('.box-content-commands').append('\n' + response + '\n');
        }
    } else {
        $('.box-content-commands').text(response);
    }

    $('.box-content-commands').append('<div class="overlay"><i class="fas fa-sync fa-spin" style="color:white"></i></div>');
    
    $.ajax({
        url: requestURL,
        type: 'POST',
        success: function (data) {
            $('.overlay').remove();
            logCommand(false, data['Result']);
        },
        error: function (jqXHR, textStatus, errorThrown) {
            $('.overlay').remove();
            logCommand(false, jqXHR.status + ' ' + errorThrown + ' - ' + jqXHR.responseJSON['Message']);
        }
    });

    if (tmpAutoClear !== 'false') $cmdInput.val('');
}
//#endregion Commands Page

//#region Global Config Utils
function generateConfigHTML(mode) {
    var namespace = mode === 'ASF' ? 'ArchiSteamFarm.GlobalConfig' : 'ArchiSteamFarm.BotConfig';
    $('.box-content-config').empty(); // Clear page content first

    $.ajax({
        url: '/Api/Type/' + namespace,
        type: 'GET',
        async: false,
        success: function (data) {
            var obj = data['Result'],
                config = obj['Body'],
                boxBodyHTML = '',
                textBoxes = '',
                checkBoxes = '',
                numberBoxes = '',
                defaultBoxes = '',
                textAreas = '';

            for (var key in config) {
                if (config.hasOwnProperty(key)) {
                    var value = config[key],
                        noSpaceKey = key.replace(/([A-Z])/g, ' $1').trim(),
                        readableKey = noSpaceKey.replace(/([A-Z])\s(?=[A-Z])/g, '$1');

                    switch (value) {
                        case 'System.Boolean':
                            checkBoxes += '<div class="">'
                                + '<button title="Toggle ' + key + '" type="button" data-type="' + value + '" class="btn btn-box-tool text-grey" id="' + key + '">'
                                + '<i id="ico' + key + '" class="fas fa-toggle-on fa-2x fa-fw fa-rotate-180" ></i ></button>'
                                + readableKey
                                + '</div>';
                            break;
                        case 'System.String':
                            textBoxes += '<div class="form-group-config">'
                                + '<label for="' + key + '">' + readableKey + '</label>'
                                + '<input type="text" id="' + key + '" class="form-control" data-type="' + value + '">'
                                + '</div>';
                            break;
                        case 'System.Byte':
                            numberBoxes += '<div class="form-group-config">'
                                + '<label for="' + key + '">' + readableKey + '</label>'
                                + '<input type="number" id="' + key + '" class="form-control" data-type="' + value + '">'
                                + '</div>';
                            break;
                        case 'System.Collections.Generic.HashSet`1[System.String]':
                            textAreas += '<div class="form-group-config">'
                                + '<label for="' + key + '">' + readableKey + '</label>'
                                + '<textarea id="' + key + '" class="form-control" data-type="' + value + '" rows="4"></textarea>'
                                + '</div>';
                            break;
                        case 'System.Collections.Generic.Dictionary`2[System.UInt64][ArchiSteamFarm.BotConfig+EPermission]':
                            textAreas += '<div class="form-group-config">'
                                + '<label for="' + key + '">' + readableKey + '</label>'
                                + '<textarea id="' + key + '" class="form-control" data-type="' + value + '" rows="3"></textarea>'
                                + '</div>';
                            break;
                        default:
                            defaultBoxes += '<div class="form-group-config">'
                                + '<label for="' + key + '">' + readableKey + '</label>'
                                + '<input type="text" id="' + key + '" class="form-control" data-type="' + value + '">'
                                + '</div>';
                    }
                }

                if (mode === 'ASF') {
                    boxBodyHTML = '<div class="col-lg-6 col-md-6 col-sm-6 col-xs-12">' + numberBoxes + '</div>'
                        + '<div class="col-lg-6 col-md-6 col-sm-6 col-xs-12">' + checkBoxes + textBoxes + defaultBoxes + textAreas + '</div>';
                } else {
                    boxBodyHTML = '<div class="col-lg-4 col-md-4 col-sm-6 col-xs-12">' + defaultBoxes + '</div>'
                        + '<div class="col-lg-4 col-md-4 col-sm-6 col-xs-12">' + textBoxes + numberBoxes + '</div>'
                        + '<div class="col-lg-4 col-md-4 col-sm-6 col-xs-12">' + checkBoxes + textAreas + '</div>';
                }
            }

            $('.box-content-config').html(boxBodyHTML);

            createClickFunction();
        }
    });
}

function createClickFunction() {
    var myNodeList = document.querySelectorAll('[data-type="System.Boolean"]');

    for (i = 0; i < myNodeList.length; i++) {
        var myID = myNodeList[i].id;

        $('#' + myID).bind('click', function () {
            var $key = $('#' + this.id);

            if ($key.hasClass('text-grey')) {
                $key.removeClass('text-grey');
                $key.addClass('text-olive');
                $('#ico' + this.id).removeClass('fa-rotate-180');
                $key.blur();
            } else {
                $key.removeClass('text-olive');
                $key.addClass('text-grey');
                $('#ico' + this.id).addClass('fa-rotate-180');
                $key.blur();
            }
        });
    }
}
//#endregion Global Config Utils

//#region Config Editor
var globalConfig = {};

function loadPageContentEditor(botName) {
    if (botName === 'ASF') {
        generateConfigHTML('ASF');
    } else {
        generateConfigHTML();
    }

    $("#saveButton").unbind();
    $("#saveButton").click(function (e) {
        swal({
            title: $.i18n('global-question-title'),
            text: $.i18n('editor-update', botName),
            type: 'warning',
            showCancelButton: true,
            confirmButtonClass: 'btn-danger',
            confirmButtonText: $.i18n('editor-update-confirm'),
            closeOnConfirm: false,
            showLoaderOnConfirm: true
        }, function () { prepareConfigForSaving(botName); });

        e.preventDefault();
    });

    $('.box-content-config').ready(function () {
        loadConfigValues(botName);
    });
}

function loadConfigValues(botName) {
    var requestURL = botName === 'ASF' ? '/Api/ASF' : '/Api/Bot/' + encodeURIComponent(botName);

    $.ajax({
        url: requestURL,
        type: 'GET',
        success: function (data) {
            var objResult = data['Result'],
                config = botName === 'ASF' ? objResult.GlobalConfig : objResult[0].BotConfig;

            globalConfig = config;

            for (var key in config) {
                if (config.hasOwnProperty(key)) {
                    var value = config[key],
                        $key = $('#' + key),
                        keyObj = $key[0];

                    if (typeof keyObj === 'undefined') continue;

                    var inputType = keyObj.dataset.type;

                    switch (inputType) {
                        case 'System.Boolean':
                            if (value) {
                                $key.removeClass('text-grey');
                                $key.addClass('text-olive');
                                $('#ico' + key).removeClass('fa-rotate-180');
                            } else {
                                $key.removeClass('text-olive');
                                $key.addClass('text-grey');
                                $('#ico' + key).addClass('fa-rotate-180');
                            }
                            break;
                        case 'System.UInt64':
                            $key.val(config['s_' + key]);
                            break;

                        case 'System.Collections.Generic.HashSet`1[System.String]':
                            $key.text(''); // Reset textarea before filling

                            for (var ipcPrefix in value) {
                                if (value.hasOwnProperty(ipcPrefix)) $key.append(value[ipcPrefix] + '\n');
                            }
                            break;
                            
                        case 'System.Collections.Generic.Dictionary`2[System.UInt64][ArchiSteamFarm.BotConfig+EPermission]':
                            $key.text(''); // Reset textarea before filling

                            for (var steamID64 in value) {
                                if (value.hasOwnProperty(steamID64)) $key.append(steamID64 + ':' + value[steamID64] + '\n');
                            }
                            break;
                        default:
                            $key.val(value);
                    }
                }
            }

            loadValuesForBotsDropDown(botName);
        }
    });
}

function loadValuesForBotsDropDown(botName) {
    var botsDropDownHTML = '';

    $.ajax({
        url: '/Api/Bot/ASF',
        type: 'GET',
        success: function (data) {
            var obj = data['Result'];

            if (botName !== 'ASF') {
                botsDropDownHTML += '<li><a href="javascript:void(0)" onclick="loadPageContentEditor(\'ASF\')">ASF</a></li>';
            }

            for (var i = 0; i < obj.length; i++) {
                var currentBot = obj[i],
                    currentBotName = currentBot.BotName;

                if (botName === currentBotName) continue;

                botsDropDownHTML += '<li><a href="javascript:void(0)" onclick="loadPageContentEditor(\'' + currentBotName + '\')">' + currentBotName + '</a></li>';
            }

            $('.box-title').html($.i18n('editor-current-bot', botName));
            $('#saveButton').data('BotName', botName);
            $('#botsDropDown').html(botsDropDownHTML);
        }
    });
}

function prepareConfigForSaving() {
    var botName = $('#saveButton').data('BotName'),
        config = globalConfig;

    for (var key in config) {
        if (config.hasOwnProperty(key)) {
            var value = config[key],
                $key = $('#' + key),
                keyObj = $key[0];

            if (typeof keyObj === 'undefined') continue;

            var inputType = keyObj.dataset.type,
                $keyValue = $key.val();

            switch (inputType) {
                case 'System.Boolean':
                    var $keyState = $('#ico' + key).hasClass('fa-rotate-180') ? false : true;
                    if ($keyState !== value) config[key] = $keyState;
                    break;

                case 'System.String':
                    if ($keyValue === '') {
                        $keyValue = null;
                        break;
                    }

                    if ($keyValue !== value) config[key] = $keyValue;
                    break;

                case 'System.UInt64':
                    if ($keyValue !== config['s_' + key]) {
                        delete config[key];
                        config['s_' + key] = $keyValue;
                    }
                    break;

                case 'System.Collections.Generic.HashSet`1[System.UInt32]':
                    if ($keyValue === '') {
                        config[key] = [];
                        break;
                    }
                    var items = $keyValue.split(',');
                    if (items.map(Number) !== value) config[key] = items.map(Number);
                    break;

                case 'System.Collections.Generic.HashSet`1[System.String]':
                    var ipcprefix = [],
                        lines = $key.val().split('\n');

                    for (var i = 0; i < lines.length; i++) {
                        if (lines[i] !== '') ipcprefix.push(lines[i]);
                    }

                    if (ipcprefix !== value) config[key] = ipcprefix;
                    break;

                case 'System.Collections.Generic.Dictionary`2[System.UInt64][ArchiSteamFarm.BotConfig+EPermission]':
                    var steamUserPermissions = {},
                        permissions = [],
                        lines = $key.val().split('\n');

                    for (var i = 0; i < lines.length; i++) {
                        if (lines[i] !== '') permissions.push(lines[i].split(':'));
                    }

                    for (var j = 0; j < permissions.length; j++) {
                        var obj = permissions[j];
                        steamUserPermissions[obj[0]] = parseInt(obj[1]);
                    }

                    if (steamUserPermissions !== value) config[key] = steamUserPermissions;
                    break;

                default:
                    if (typeof value === 'object') {
                        var objItems = $keyValue.split(',');
                        if (objItems.map(Number) !== value) config[key] = objItems.map(Number);
                    } else if (typeof value === 'number') {
                        var number = Number($keyValue);
                        if (number !== value) config[key] = number;
                    } else {
                        if ($keyValue !== value) config[key] = $keyValue;
                    }
            }
        }
    }

    config = botName === 'ASF' ? { GlobalConfig: config } : { BotConfig: config };

    saveConfig(botName, config);
}

function saveConfig(botName, config) {
    var requestURL = botName === 'ASF' ? '/Api/ASF' : '/Api/Bot/' + encodeURIComponent(botName);

    $.ajax({
        url: requestURL,
        type: 'POST',
        data: JSON.stringify(config, null, "\t"),
        contentType: 'application/json',
        success: function (data) {
            swal({
                title: $.i18n('global-success-title'),
                text: $.i18n('editor-save-confirm', botName),
                type: 'success'
            }, function () { location.reload(); });
        },
        error: function (jqXHR, textStatus, errorThrown) {
            swal({
                title: $.i18n('global-error-title'),
                text: jqXHR.status + ' ' + errorThrown + ' - ' + jqXHR.responseJSON['Message'],
                type: 'error'
            }, function () { location.reload(); });
        }
    });
}
//#endregion Config Editor

//#region Config Generator
var globalDefaultConfig = {};

function loadPageContentGenerator(mode) {
    if (mode === 'ASF') {
        generateConfigHTML('ASF');
        $('#GeneratorName').hide();
    } else {
        generateConfigHTML();
        $('#GeneratorName').show();
    }

    $("#downloadButton").unbind();
    $("#downloadButton").click(function (e) {
        prepareConfigForDownload(mode);
        e.preventDefault();
    });

    $('.box-content-config').ready(function () {
        loadDefaultConfigValues(mode);
    });
}

function loadDefaultConfigValues(mode) {
    var namespace = mode === 'ASF' ? 'ArchiSteamFarm.GlobalConfig' : 'ArchiSteamFarm.BotConfig';

    $.ajax({
        url: '/Api/Structure/' + namespace,
        type: 'GET',
        success: function (data) {
            var config = data['Result'];

            globalDefaultConfig = config;

            for (var key in config) {
                if (config.hasOwnProperty(key)) {
                    var value = config[key],
                        $key = $('#' + key),
                        keyObj = $key[0];

                    if (typeof keyObj === 'undefined') continue;

                    var inputType = keyObj.dataset.type;

                    switch (inputType) {
                        case 'System.Boolean':
                            if (value) {
                                $key.removeClass('text-grey');
                                $key.addClass('text-olive');
                                $('#ico' + key).removeClass('fa-rotate-180');
                            } else {
                                $key.removeClass('text-olive');
                                $key.addClass('text-grey');
                                $('#ico' + key).addClass('fa-rotate-180');
                            }
                            break;
                        case 'System.UInt64':
                            $key.val(config['s_' + key]);
                            break;
                        case 'System.Collections.Generic.Dictionary`2[System.UInt64][ArchiSteamFarm.BotConfig+EPermission]':
                            $key.text(''); // Reset textarea before filling

                            for (var steamID64 in value) {
                                if (value.hasOwnProperty(steamID64)) $key.append(steamID64 + ':' + value[steamID64] + '\n');
                            }
                            break;
                        default:
                            $key.val(value);
                    }
                }
            }

            loadValuesForModeDropDown(mode);
        }
    });
}

function loadValuesForModeDropDown(mode) {
    var botsDropDownHTML = '';

    mode = mode !== 'ASF' ? 'Bot' : 'ASF';

    if (mode === 'ASF') {
        botsDropDownHTML = '<li><a href="javascript:void(0)" onclick="loadPageContentGenerator();">Bot</a></li>';
    } else {
        botsDropDownHTML = '<li><a href="javascript:void(0)" onclick="loadPageContentGenerator(\'ASF\');">ASF</a></li>';
    }

    $('.box-title').html($.i18n('generator-current-bot', mode));
    $('#modeDropDown').html(botsDropDownHTML);
}

function prepareConfigForDownload(mode) {
    var config = globalDefaultConfig;

    if (mode !== 'ASF') {
        var botName = $('#GeneratorName').val();

        if (botName === '') {
            swal({
                title: $.i18n('global-error-title'),
                text: $.i18n('generator-name'),
                type: 'error'
            });
            return false;
        }

        if (botName.substr(botName.length - 5) === '.json') {
            botName = botName.substr(0, botName.length - 5);
        }
    }

    for (var key in config) {
        if (config.hasOwnProperty(key)) {
            var value = config[key],
                $key = $('#' + key),
                keyObj = $key[0];

            if (typeof keyObj === 'undefined') continue;

            var inputType = keyObj.dataset.type,
                $keyValue = $key.val();

            switch (inputType) {
                case 'System.Boolean':
                    var $keyState = $('#ico' + key).hasClass('fa-rotate-180') ? false : true;
                    if ($keyState !== value) config[key] = $keyState;
                    break;

                case 'System.String':
                    if ($keyValue === '') $keyValue = null;
                    if ($keyValue !== value) config[key] = $keyValue;
                    break;

                case 'System.UInt64':
                    if ($keyValue !== config['s_' + key]) {
                        delete config[key];
                        config['s_' + key] = $keyValue;
                    }
                    break;

                case 'System.Collections.Generic.HashSet`1[System.UInt32]':
                    if ($keyValue === '') continue;
                    var items = $keyValue.split(',');
                    if (items.map(Number) !== value) config[key] = items.map(Number);
                    break;

                case 'System.Collections.Generic.HashSet`1[System.String]':
                    var ipcprefix = [],
                        lines = $key.val().split('\n');

                    for (var i = 0; i < lines.length; i++) {
                        if (lines[i] !== '') ipcprefix.push(lines[i]);
                    }

                    if (ipcprefix !== value) config[key] = ipcprefix;
                    break;

                case 'System.Collections.Generic.Dictionary`2[System.UInt64][ArchiSteamFarm.BotConfig+EPermission]':
                    var steamUserPermissions = {},
                        permissions = [],
                        lines = $key.val().split('\n');

                    for (var i = 0; i < lines.length; i++) {
                        if (lines[i] !== '') permissions.push(lines[i].split(':'));
                    }

                    for (var j = 0; j < permissions.length; j++) {
                        var obj = permissions[j];
                        steamUserPermissions[obj[0]] = parseInt(obj[1]);
                    }

                    if (steamUserPermissions !== value) config[key] = steamUserPermissions;
                    break;

                default:
                    if (typeof value === 'object') {
                        var objItems = $keyValue.split(',');
                        if (objItems.map(Number) !== value) config[key] = objItems.map(Number);
                    } else if (typeof value === 'number') {
                        var number = Number($keyValue);
                        if (number !== value) config[key] = number;
                    } else {
                        if ($keyValue !== value) config[key] = $keyValue;
                    }
            }
        }
    }

    if (mode !== 'ASF') {
        downloadObjectAsJson(botName, config);
        $('#GeneratorName').val('');
    } else {
        downloadObjectAsJson('ASF', config);
    }
}

function downloadObjectAsJson(exportName, exportObj) {
    var dataStr = "data:text/json;charset=utf-8," + encodeURIComponent(JSON.stringify(exportObj, null, "\t"));
    var downloadAnchorNode = document.createElement('a');
    downloadAnchorNode.setAttribute("href", dataStr);
    downloadAnchorNode.setAttribute("download", exportName + ".json");
    downloadAnchorNode.click();
    downloadAnchorNode.remove();
}
//#endregion Config Page

//#region Right Sidebar
$(function () {
    'use strict';

    var mySkins = [
        'skin-blue',
        'skin-teal',
        'skin-black',
        'skin-red',
        'skin-yellow',
        'skin-purple',
        'skin-green'
    ];

    function changeSkin(cls) {
        $.each(mySkins, function (i) {
            $('body').removeClass(mySkins[i]);
            $('[data-skin="' + mySkins[i] + '"]').removeClass('btn-badge-active');
        });

        $('body').addClass(cls);
        $('[data-skin="' + cls + '"]').addClass('btn-badge-active');
        store('skin', cls);
        return false;
    }

    function changeBoxed(savedLayout) {
        if (savedLayout === 'layout-boxed') {
            if ($('body').hasClass('fixed')) {
                $('body').removeClass('fixed');
                $('body').addClass('layout-boxed');
                $('#toggleBoxed').removeClass('text-grey');
                $('#toggleBoxed').addClass('text-olive');
                $('#iconBoxed').removeClass('fa-rotate-180');
            }
        }
    }

    function toggleBoxed() {
        if ($('body').hasClass('fixed')) {
            $('body').removeClass('fixed');
            $('body').addClass('layout-boxed');
            $('#toggleBoxed').removeClass('text-grey');
            $('#toggleBoxed').addClass('text-olive');
            $('#iconBoxed').removeClass('fa-rotate-180');
            $('#toggleBoxed').blur();
            store('layoutState', 'layout-boxed');
        } else {
            $('body').removeClass('layout-boxed');
            $('body').addClass('fixed');
            $('#toggleBoxed').removeClass('text-olive');
            $('#toggleBoxed').addClass('text-grey');
            $('#iconBoxed').addClass('fa-rotate-180');
            $('#toggleBoxed').blur();
            store('layoutState', 'fixed');
        }
    }

    function changeNightmode(savedNightmodeState) {
        if (savedNightmodeState === 'nightmode') {
            $('body').addClass('nightmode');
            $('#toggleNightmode').removeClass('text-grey');
            $('#toggleNightmode').addClass('text-olive');
            $('#iconNightmode').removeClass('fa-rotate-180');
        }
    }

    function toggleNightmode() {
        if ($('body').hasClass('nightmode')) {
            $('body').removeClass('nightmode');
            $('#toggleNightmode').removeClass('text-olive');
            $('#toggleNightmode').addClass('text-grey');
            $('#iconNightmode').addClass('fa-rotate-180');
            $('#toggleNightmode').blur();
            store('nightmodeState', null);
        } else {
            $('body').addClass('nightmode');
            $('#toggleNightmode').removeClass('text-grey');
            $('#toggleNightmode').addClass('text-olive');
            $('#iconNightmode').removeClass('fa-rotate-180');
            $('#toggleNightmode').blur();
            store('nightmodeState', 'nightmode');
        }
    }

    function loadLanguageHTML() {
        var tmpLangCode = get('langCode'),
            tmpLangMissing = get('langMissing'),
            tmpLangTotal = get('langTotal');

        $('#currentLanguage').attr({
            alt: tmpLangCode,
            src: '../img/flags/' + tmpLangCode + '.gif'
        });

        if (tmpLangMissing > 0) {
            var percentage = (tmpLangMissing * 100 / tmpLangTotal).toFixed(0),
                //infoText = $.i18n('global-language-info', percentage); //Fix this
                infoText = percentage + "% of this language is not translated!";
            $('#languageInfo').html('<div class="alert alert-warning alert-dismissible">'
                + '<button data-i18n="title-global-never" title="Never show again" type="button" class="close" data-dismiss="alert" aria-hidden="true">x</button>'
                + infoText
                + '</div>');
        } else {
            $('#languageInfo').text('');
        }        

        $('#languages').collapse('hide');
    }

    function loadLayout() {
        var tmpSkin = get('skin'),
            tmpLayoutState = get('layoutState'),
            tmpNightmodeState = get('nightmodeState'),
            tmpLeftSidebarState = get('leftSidebarState');

        if (tmpSkin && $.inArray(tmpSkin, mySkins)) changeSkin(tmpSkin);            
        if (tmpLeftSidebarState === 'sidebar-collapse') {
            $('body').addClass('sidebar-collapse');
        } 
        if (tmpLayoutState) changeBoxed(tmpLayoutState);
        if (tmpNightmodeState) changeNightmode(tmpNightmodeState);

        loadLanguageHTML();

        $('[data-skin]').on('click', function (e) {
            e.preventDefault();
            changeSkin($(this).data('skin'));
        });

        $('#toggleBoxed').on('click', function (e) {
            e.preventDefault();
            toggleBoxed();
        });

        $('#toggleNightmode').on('click', function (e) {
            e.preventDefault();
            toggleNightmode();
        });

        $('#leftSidebar').on('click', function (e) {
            e.preventDefault();
            var state = $('body').hasClass('sidebar-collapse') ? 'normal' : 'sidebar-collapse';
            store('leftSidebarState', state);
        });

        $('.language').on('click', function (e) {
            e.preventDefault();
            loadLocales($(this).data('locale'));
            loadLanguageHTML();
        });
    }

    // Create the menu
    var $layoutSettings = $('<div />');

    // Layout options
    $layoutSettings.append(
        '<h4 class="control-sidebar-heading" data-i18n="global-layout">Layout</h4>'
        // Boxed Layout
        + '<div class="form-group hidden-xs hidden-sm">'
        + '<label class="control-sidebar-subheading">'
        + '<button data-i18n="title-global-boxed" title="Toggle boxed layout" type="button" class="btn btn-box-tool pull-right text-grey" id="toggleBoxed"><i id="iconBoxed" class="fas fa-toggle-on fa-2x fa-rotate-180"></i></button>'
        + '<i class="far fa-square fa-fw"></i> <span data-i18n="global-boxed">Boxed Layout</span>'
        + '</label>'
        + '<p data-i18n="global-boxed-description">Toggle the boxed layout</p>'
        + '</div>'
        // Nightmode
        + '<div class="form-group">'
        + '<label class="control-sidebar-subheading">'
        + '<button data-i18n="title-global-nightmode" title="Toggle nightmode" type="button" class="btn btn-box-tool pull-right text-grey" id="toggleNightmode"><i id="iconNightmode" class="fas fa-toggle-on fa-2x fa-rotate-180"></i></button>'
        + '<i class="fas fa-moon fa-fw"></i> <span data-i18n="global-nightmode">Nightmode</span>'
        + '</label>'
        + '<p data-i18n="global-nightmode-description">Toggle the nightmode</p>'
        + '</div>'
    );
    
    var $skinsList = $('<ul />', { 'class': 'list-unstyled clearfix text-center' });
    
    var $skinBlue = $('<li />', { style: 'float:left; width: 14%;' })
        .append('<button data-skin="skin-blue" class="clearfix full-opacity-hover btn btn-badge bg-blue"></button>');
    $skinsList.append($skinBlue);
    var $skinBlack = $('<li />', { style: 'float:left; width: 14%;' })
        .append('<button data-skin="skin-black" class="clearfix full-opacity-hover btn btn-badge bg-black"></button>');
    $skinsList.append($skinBlack);
    var $skinPurple = $('<li />', { style: 'float:left; width: 14%;' })
        .append('<button data-skin="skin-purple" class="clearfix full-opacity-hover btn btn-badge bg-purple"></button>');
    $skinsList.append($skinPurple);
    var $skinGreen = $('<li />', { style: 'float:left; width: 14%;' })
        .append('<button data-skin="skin-green" class="clearfix full-opacity-hover btn btn-badge bg-green"></button>');
    $skinsList.append($skinGreen);
    var $skinRed = $('<li />', { style: 'float:left; width: 14%;' })
        .append('<button data-skin="skin-red" class="clearfix full-opacity-hover btn btn-badge bg-red"></button>');
    $skinsList.append($skinRed);
    var $skinYellow = $('<li />', { style: 'float:left; width: 14%;' })
        .append('<button data-skin="skin-yellow" class="clearfix full-opacity-hover btn btn-badge bg-yellow"></button>');
    $skinsList.append($skinYellow);
    var $skinTeal = $('<li />', { style: 'float:left; width: 14%;' })
        .append('<button data-skin="skin-teal" class="clearfix full-opacity-hover btn btn-badge bg-teal"></button>');
    $skinsList.append($skinTeal);

    $layoutSettings.append('<h4 class="control-sidebar-heading" data-i18n="global-skins">Skins</h4>');
    $layoutSettings.append($skinsList);

    var $languagesList = $('<div />', { 'class': 'collapse', 'id': 'languages' });

    loadAllLanguages();

    for (var i in availableLanguages) {
        var language = availableLanguages[i],
            langCode = (language === 'strings') ? 'us' : language.substr(language.length - 2).toLowerCase();

        $languagesList.append('<button data-i18n="title-global-language" title="Change language" type="button" class="btn btn-box-tool language" data-locale="' + language + '"><img src="../img/flags/' + langCode + '.gif" alt="' + langCode + '"></button>');
    }

    $layoutSettings.append('<h4 class="control-sidebar-heading" data-i18n="global-language">Language</h4>'
        + '<div id="languageInfo"></div>'
        + '<div class="form-group">'
        + '<label class="control-sidebar-subheading">'
        + '<button data-i18n="title-global-language" title="Change language" type="button" class="btn btn-box-tool pull-right" data-toggle="collapse" data-target="#languages"><span data-i18n="global-change">Change</span> <i class="fas fa-caret-down"></i></button>'
        + '<img id="currentLanguage" src="../img/flags/us.gif" alt="us">'
        + '</label>'
        + '</div>'
    );

    $layoutSettings.append($languagesList);

    $('#control-right-sidebar').after($layoutSettings);

    loadLayout();
});
//#endregion Right Sidebar
