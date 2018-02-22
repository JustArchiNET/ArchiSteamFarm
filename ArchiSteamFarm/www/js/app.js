//#region Utils
if (typeof jQuery === 'undefined') throw new Error('ASF App requires jQuery');

function get(name) {
    if (typeof Storage !== 'undefined') {
        return localStorage.getItem(name);
    } else {
        window.alert('Please use a modern browser to properly view ASF GUI!');
    }
}

function store(name, val) {
    if (typeof Storage !== 'undefined') {
        localStorage.setItem(name, val);
    } else {
        window.alert('Please use a modern browser to properly view ASF GUI!');
    }
}

function getIPCPassword() {
    swal({
        title: 'IPC password required',
        text: 'Please enter the correct IPC password:',
        type: 'input',
        showCancelButton: true,
        closeOnConfirm: false,
        inputPlaceholder: 'Type your password',
        inputType: 'password'
    }, function (typedPassword) {
        if (typedPassword === false) return false;

        if (typedPassword === '') {
            swal.showInputError('You need to enter a valid password!');
            return false;
        }

        store('IPCPassword', typedPassword);
        swal({
            title: 'Success!',
            text: 'Your IPC password has been saved.',
            type: 'success'
        }, function () { location.reload(); });
    });
}

var IPCPassword = get('IPCPassword');
if (IPCPassword) $.ajaxSetup({ beforeSend: function (jqXHR) { jqXHR.setRequestHeader('Authentication', IPCPassword); } });
//#endregion Utils

//#region Footer
$('.main-footer').ready(function () {
    $.ajax({
        url: '/Api/ASF',
        type: 'GET',
        statusCode: { 401: function () { getIPCPassword(); } },
        success: function (data) {
            var obj = data['Result'].Version,
                version = obj.Major + '.' + obj.Minor + '.' + obj.Build + '.' + obj.Revision;
            
            $('#version').html('<b>Version</b> ' + version);
            $('#changelog').attr('href', 'https://github.com/JustArchi/ArchiSteamFarm/releases/tag/' + version);
        }
    });
});
//#endregion Footer

//#region Bot Status Buttons
$('.bot-status').ready(function () {
    function displayBotStatus() {
        var activeBots = 0,
            idleBots = 0,
            offlineBots = 0;

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
                        offlineBots++;
                    } else {
                        if (TimeRemaining === '00:00:00') {
                            idleBots++;
                        } else {
                            activeBots++;
                        }
                    }
                }

                $('#offlineBots').text(offlineBots);
                $('#idleBots').text(idleBots);
                $('#activeBots').text(activeBots);
            }
        });
    }

    displayBotStatus();
    window.setInterval(function () { displayBotStatus(); }, 5000);
});
//#endregion Bot Status Buttons

//#region ASF Information
$('.info-overview').ready(function () {
    function displayRAMUsage() {
        $.ajax({
            url: '/Api/ASF',
            type: 'GET',
            success: function (data) { $('#ramUsage').html((data['Result'].MemoryUsage / 1024).toFixed(2) + ' MB'); }
        });
    }

    displayRAMUsage();
    window.setInterval(function () { displayRAMUsage(); }, 10000);
    
    function displayUptime() {
        $.ajax({
            url: '/Api/ASF',
            type: 'GET',
            success: function (data) { $('#uptime').html(uptimeToString(data['Result'].ProcessStartTime)); }
        });
    }

    displayUptime();
    window.setInterval(function () { displayUptime(); }, 60000);
});

function uptimeToString(startTime) {
    var processStartTime = new Date(startTime),
        currentDate = new Date(),
        diff = currentDate.getTime() - processStartTime.getTime();

    var days = Math.floor(diff / (1000 * 60 * 60 * 24));
    diff -= days * (1000 * 60 * 60 * 24);

    var hours = Math.floor(diff / (1000 * 60 * 60));
    diff -= hours * (1000 * 60 * 60);

    var mins = Math.floor(diff / (1000 * 60));

    hours = (hours < 10 ? '0' : '') + hours;
    mins = (mins < 10 ? '0' : '') + mins;

    return days + 'd ' + hours + 'h ' + mins + 'm';
}
//#endregion ASF Information

//#region Command Page
var $cmdInput = $('#commandInput');
function fillCommand(cmd) { $cmdInput.val(cmd + ' '); }
function fillBots(bot) { $cmdInput.val($cmdInput.val() + bot); }

function getDateAndTime() {
    var currentdate = new Date();
    return ('0' + currentdate.getDate()).slice(-2) + '.'
        + ('0' + (currentdate.getMonth() + 1)).slice(-2) + '.'
        + currentdate.getFullYear() + ' @ '
        + ('0' + currentdate.getHours()).slice(-2) + ':'
        + ('0' + currentdate.getMinutes()).slice(-2) + ':'
        + ('0' + currentdate.getSeconds()).slice(-2);
}

function logCommand(state, cmd) {
    var tmpAutoClear = get('autoClear');

    if (state) {
        $('#commandSent').val(getDateAndTime() + ' Command sent: ' + cmd);
    } else {
        if (tmpAutoClear === 'false') {
            $('.box-content-command').append('\n' + getDateAndTime() + ' Response received: ' + cmd + '\n');
        } else {
            $('.box-content-command').text(getDateAndTime() + ' Response received: ' + cmd);
        }
    }
}

function sendCommand() {
    var command = $cmdInput.val(),
        requestURL = '/Api/Command/' + command,
        tmpAutoClear = get('autoClear');

    if (command === '') return;

    logCommand(true, command);

    if (tmpAutoClear === 'false') {
        if ($('.box-content-command').text() === '') {
            $('.box-content-command').append(getDateAndTime() + ' Waiting for response...' + '\n');
        } else {
            $('.box-content-command').append('\n' + getDateAndTime() + ' Waiting for response...' + '\n');
        }

    } else {
        $('.box-content-command').text(getDateAndTime() + ' Waiting for response...');
    }

    $('.box-content-command').append('<div class="overlay"><i class="fas fa-sync fa-spin" style="color:white"></i></div>');

    $.ajax({
        url: requestURL,
        type: 'GET',
        success: function (data) {
            $('.overlay').remove();
            logCommand(false, data['Result']);
        },
        error: function (jqXHR, textStatus, errorThrown) {
            $('.overlay').remove();
            logCommand(false, jqXHR.status + ' - ' + errorThrown);
        }
    });

    if (tmpAutoClear !== 'false') $cmdInput.val('');
}
//#endregion Command Page

//#region Config Page

//#region New stuff
//const cachedTypeDefinitions = new Map();
//const cachedStructureDefinitions = new Map();

//function request(method, url, data) {
//    return new Promise((resolve, reject) => {
//        $.ajax(url, { method, data })
//            .done(resolve)
//            .fail(reject);
//    });
//}

//function extract(key) {
//    return obj => obj[key];
//}

//const API = {
//    get: (endpoint, data) => request('GET', `/Api/${endpoint}`, data).then(extract('Result')),
//    post: (endpoint, data) => request('POST', `/Api/${endpoint}`, data).then(extract('Result'))
//};

//const subtypeRegex = /\[[^\]]+\]/g;

//function resolveSubtypes(type) {
//    return type.match(subtypeRegex).map(subtype => subtype.slice(1, subtype.length - 1));
//}

//async function getStructureDefinition(type) {
//    if (cachedStructureDefinitions.has(type)) return cachedStructureDefinitions.get(type);

//    const structureDefinition = API.get(`Structure/${encodeURIComponent(type)}`);
//    cachedStructureDefinitions.set(type, structureDefinition);

//    return structureDefinition;
//}

//async function getTypeDefinition(type) {
//    if (cachedTypeDefinitions.has(type)) return cachedTypeDefinitions.get(type);

//    const typeDefinition = API.get(`Type/${encodeURIComponent(type)}`);
//    cachedTypeDefinitions.set(type, typeDefinition);

//    return typeDefinition;
//}

//async function resolveType(type) {
//    switch (type.split('`')[0]) {
//        case 'System.Boolean':
//            return { type: 'boolean' };
//        case 'System.String':
//            return { type: 'string' };
//        case 'System.Byte':
//            return { type: 'smallNumber' };
//        case 'System.UInt32':
//            return { type: 'number' };
//        case 'System.Collections.Generic.HashSet':
//            const [subtype] = resolveSubtypes(type);
//            return { type: 'hashSet', values: await resolveType(subtype) };
//        case 'System.UInt64':
//            return { type: 'bigNumber' };
//        case 'System.Collections.Generic.Dictionary':
//            const subtypes = resolveSubtypes(type);
//            return { type: 'dictionary', key: await resolveType(subtypes[0]), value: await resolveType(subtypes[1]) };
//        default: // Complex type
//            return unwindType(type);
//    }
//}

//async function unwindObject(type, typeDefinition) {
//    const resolvedStructure = {
//        type: 'object',
//        body: {}
//    };

//    const [structureDefinition, resolvedTypes] = await Promise.all([
//        getStructureDefinition(type),
//        Promise.all(Object.keys(typeDefinition.Body).map(async param => ({ param, type: await resolveType(typeDefinition.Body[param]) })))
//    ]);

//    for (const { param, type } of resolvedTypes) {
//        const paramName = typeDefinition.Body[param] !== 'System.UInt64' ? param : `s_${param}`;

//        resolvedStructure.body[param] = {
//            defaultValue: structureDefinition[param],
//            paramName,
//            ...type
//        };
//    }

//    return resolvedStructure;
//}

//async function unwindType(type) {
//    if (type === 'ArchiSteamFarm.BotConfig') getStructureDefinition(type); // Dirty trick, but 30% is 30%
//    const typeDefinition = await getTypeDefinition(type);

//    switch (typeDefinition.Properties.BaseType) {
//        case 'System.Object':
//            return unwindObject(type, typeDefinition);
//        case 'System.Enum':
//            return { type: (typeDefinition.Properties.CustomAttributes || []).includes('System.FlagsAttribute') ? 'flag' : 'enum', values: typeDefinition.Body };
//        default:
//            const structureDefinition = await getStructureDefinition(type);
//            return { type: 'unknown', typeDefinition, structureDefinition };
//    }
//}
//#endregion New stuff

var globalBotConfig = {},
    globalDefaultConfig = {};

function generateConfigHTML(prefix) {
    $('#config' + prefix + 'Tab').empty(); // Clear page content first

    $.ajax({
        url: '/Api/Type/ArchiSteamFarm.BotConfig',
        type: 'GET',
        success: function (data) {
            var obj = data['Result'],
                objBody = obj['Body'],
                boxBodyHTML = '',
                textBoxes = '',
                checkBoxes = '',
                numberBoxes = '',
                defaultBoxes = '',
                textAreas = '';

            var nameBoxHTML = prefix === 'Generator' ? '<div class="form-group-config"><label for="GeneratorName">Name</label><input type="text" id="GeneratorName" class="form-control"></div>' : '';

            for (var key in objBody) {
                if (objBody.hasOwnProperty(key)) {
                    var value = objBody[key],
                        noSpaceKey = key.replace(/([A-Z])/g, ' $1').trim(),
                        readableKey = noSpaceKey.replace(/([A-Z])\s(?=[A-Z])/g, '$1');

                    switch (value) {
                        case 'System.Boolean':
                            checkBoxes += '<div class="">'
                                + '<button title="Toggle ' + key + '" type="button" data-type="' + value + '" class="btn btn-box-tool text-grey" id="' + prefix + key + '"><i id="ico' + prefix + key + '" class="fas fa-toggle-on fa-2x fa-fw fa-rotate-180"></i></button>'
                                + readableKey
                                + '</div>';
                            break;
                        case 'System.String':
                            textBoxes += '<div class="form-group-config">'
                                + '<label for="' + prefix + key + '">' + readableKey + '</label>'
                                + '<input type="text" id="' + prefix + key + '" class="form-control" data-type="' + value + '">'
                                + '</div>';
                            break;
                        case 'System.Byte':
                            numberBoxes += '<div class="form-group-config">'
                                + '<label for="' + prefix + key + '">' + readableKey + '</label>'
                                + '<input type="number" id="' + prefix + key + '" class="form-control" data-type="' + value + '">'
                                + '</div>';
                            break;
                        case 'System.Collections.Generic.Dictionary`2[System.UInt64][ArchiSteamFarm.BotConfig+EPermission]':
                            textAreas += '<div class="form-group-config">'
                                + '<label for="' + prefix + key + '">' + readableKey + '</label>'
                                + '<textarea id="' + prefix + key + '" class="form-control" data-type="' + value + '" rows="3"></textarea>'
                                + '</div>';
                            break;
                        default:
                            defaultBoxes += '<div class="form-group-config">'
                                + '<label for="' + prefix + key + '">' + readableKey + '</label>'
                                + '<input type="text" id="' + prefix + key + '" class="form-control" data-type="' + value + '">'
                                + '</div>';
                    }
                }

                boxBodyHTML = '<div class="col-lg-4 col-md-4 col-sm-6 col-xs-12">' + nameBoxHTML + defaultBoxes + '</div>'
                    + '<div class="col-lg-4 col-md-4 col-sm-6 col-xs-12">' + textBoxes + numberBoxes + '</div>'
                    + '<div class="col-lg-4 col-md-4 col-sm-6 col-xs-12">' + checkBoxes + textAreas + '</div>';
            }

            var boxHeaderHTML = prefix === 'Changer' ? '<div class="box-header with-border">'
                + '<h3 class="box-title"></h3>'
                + '<div class="box-tools pull-right">'
                + '<div class="btn-group">'
                + '<button type="button" class="btn btn-box-tool dropdown-toggle" data-toggle="dropdown" aria-expanded="false">'
                + 'Change bot '
                + '<span class="fas fa-caret-down"></span>'
                + '</button>'
                + '<ul class="dropdown-menu scrollable-menu" role="menu" id="botsDropDown"></ul>'
                + '</div>'
                + '</div>'
                + '</div>' : '';

            $('#config' + prefix + 'Tab').html(boxHeaderHTML + '<div class="box-body">' + boxBodyHTML + '</div>');

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

function loadConfigValuesForBot(botName) {
    $.ajax({
        url: '/Api/Bot/' + encodeURIComponent(botName),
        type: 'GET',
        success: function (data) {
            var obj = data['Result'],
                objBot = obj[0],
                BotConfig = objBot.BotConfig;

            globalBotConfig = BotConfig;

            for (var key in BotConfig) {
                if (BotConfig.hasOwnProperty(key)) {
                    var value = BotConfig[key],
                        $key = $('#Changer' + key),
                        keyObj = $key[0];

                    if (typeof keyObj === 'undefined') continue;

                    var inputType = keyObj.dataset.type;

                    switch (inputType) {
                        case 'System.Boolean':
                            if (value) {
                                $key.removeClass('text-grey');
                                $key.addClass('text-olive');
                                $('#icoChanger' + key).removeClass('fa-rotate-180');
                            } else {
                                $key.removeClass('text-olive');
                                $key.addClass('text-grey');
                                $('#icoChanger' + key).addClass('fa-rotate-180');
                            }
                            break;
                        case 'System.UInt64':
                            $key.val(BotConfig['s_' + key]);
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
            
            loadBotsDropDown(botName);
        }
    });
}

function loadDefaultConfigValues() {
    $.ajax({
        url: '/Api/Structure/ArchiSteamFarm.BotConfig',
        type: 'GET',
        success: function (data) {
            var BotConfig = data['Result'];

            globalDefaultConfig = BotConfig;

            for (var key in BotConfig) {
                if (BotConfig.hasOwnProperty(key)) {
                    var value = BotConfig[key],
                        $key = $('#Generator' + key),
                        keyObj = $key[0];

                    if (typeof keyObj === 'undefined') continue;

                    var inputType = keyObj.dataset.type;

                    switch (inputType) {
                        case 'System.Boolean':
                            if (value) {
                                $key.removeClass('text-grey');
                                $key.addClass('text-olive');
                                $('#icoGenerator' + key).removeClass('fa-rotate-180');
                            } else {
                                $key.removeClass('text-olive');
                                $key.addClass('text-grey');
                                $('#icoGenerator' + key).addClass('fa-rotate-180');
                            }
                            break;
                        case 'System.UInt64':
                            $key.val(BotConfig['s_' + key]);
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
        }
    });
}

function prepareBotConfigForSaving() {
    var botName = $('#saveButton').data('BotName'),
        BotConfig = globalBotConfig;

    for (var key in BotConfig) {
        if (BotConfig.hasOwnProperty(key)) {
            var value = BotConfig[key],
                $key = $('#Changer' + key),
                keyObj = $key[0];

            if (typeof keyObj === 'undefined') continue;

            var inputType = keyObj.dataset.type,
                $keyValue = $key.val();

            switch (inputType) {
                case 'System.Boolean':
                    var $keyState = $('#icoChanger' + key).hasClass('fa-rotate-180') ? false : true;
                    if ($keyState !== value) BotConfig[key] = $keyState;
                    break;

                case 'System.String':
                    if ($keyValue === '') $keyValue = null;
                    if ($keyValue !== value) BotConfig[key] = $keyValue;
                    break;
                case 'System.UInt64':
                    if ($keyValue !== BotConfig['s_' + key]) {
                        delete BotConfig[key];
                        BotConfig['s_' + key] = $keyValue;
                    }
                    break;
                case 'System.Collections.Generic.HashSet`1[System.UInt32]':
                    var items = $keyValue.split(',');
                    if (items.map(Number) !== value) BotConfig[key] = items.map(Number);
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

                    if (steamUserPermissions !== value) BotConfig[key] = steamUserPermissions;
                    break;

                default:
                    if (typeof value === 'object') {
                        var objItems = $keyValue.split(',');
                        if (objItems.map(Number) !== value) BotConfig[key] = objItems.map(Number);
                    } else if (typeof value === 'number') {
                        var number = Number($keyValue);
                        if (number !== value) BotConfig[key] = number;
                    } else {
                        if ($keyValue !== value) BotConfig[key] = $keyValue;
                    }
            }
        }
    }

    saveConfig(botName, { BotConfig });
}

function prepareGeneratorConfigForSaving() {
    var botName = $('#GeneratorName').val(),
        BotConfig = globalDefaultConfig;

    if (botName === '') {
        swal({
            title: 'Error!',
            text: 'You need to enter a name',
            type: 'error'
        });
        return false;
    }

    for (var key in BotConfig) {
        if (BotConfig.hasOwnProperty(key)) {
            var value = BotConfig[key],
                $key = $('#Generator' + key),
                keyObj = $key[0];

            if (typeof keyObj === 'undefined') continue;

            var inputType = keyObj.dataset.type,
                $keyValue = $key.val();

            switch (inputType) {
                case 'System.Boolean':
                    var $keyState = $('#icoGenerator' + key).hasClass('fa-rotate-180') ? false : true;
                    if ($keyState !== value) BotConfig[key] = $keyState;
                    break;

                case 'System.String':
                    if ($keyValue === '') $keyValue = null;
                    if ($keyValue !== value) BotConfig[key] = $keyValue;
                    break;
                case 'System.UInt64':
                    if ($keyValue !== BotConfig['s_' + key]) {
                        delete BotConfig[key];
                        BotConfig['s_' + key] = $keyValue;
                    }
                    break;
                case 'System.Collections.Generic.HashSet`1[System.UInt32]':
                    var items = $keyValue.split(',');
                    if (items.map(Number) !== value) BotConfig[key] = items.map(Number);
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

                    if (steamUserPermissions !== value) BotConfig[key] = steamUserPermissions;
                    break;

                default:
                    if (typeof value === 'object') {
                        var objItems = $keyValue.split(',');
                        if (objItems.map(Number) !== value) BotConfig[key] = objItems.map(Number);
                    } else if (typeof value === 'number') {
                        var number = Number($keyValue);
                        if (number !== value) BotConfig[key] = number;
                    } else {
                        if ($keyValue !== value) BotConfig[key] = $keyValue;
                    }
            }
        }
    }
    
    downloadObjectAsJson(botName, BotConfig);
}

function downloadObjectAsJson(exportName, exportObj) {
    var dataStr = "data:text/json;charset=utf-8," + encodeURIComponent(JSON.stringify(exportObj));
    var downloadAnchorNode = document.createElement('a');
    downloadAnchorNode.setAttribute("href", dataStr);
    downloadAnchorNode.setAttribute("download", exportName + ".json");
    downloadAnchorNode.click();
    downloadAnchorNode.remove();
}

function saveConfig(botName, config) {
    $.ajax({
        url: '/Api/Bot/' + encodeURIComponent(botName),
        type: 'POST',
        data: JSON.stringify(config),
        contentType: 'application/json',
        success: function (data) {
            swal({
                title: 'Success!',
                text: '<' + botName + '> and its config file got updated.',
                type: 'success'
            }, function () { location.reload(); });
        },
        error: function (jqXHR, textStatus, errorThrown) {
            swal({
                title: 'Error!',
                text: jqXHR.status + ' - ' + errorThrown,
                type: 'error'
            }, function () { location.reload(); });
        }
    });
}

function loadBotsDropDown(botName) {
    var botsDropDownHTML = '';

    $.ajax({
        url: '/Api/Bot/ASF',
        type: 'GET',
        success: function (data) {
            var obj = data['Result'];

            for (var i = 0; i < obj.length; i++) {
                var currentBot = obj[i],
                    currentBotName = currentBot.BotName;

                if (botName === currentBotName) continue;

                botsDropDownHTML += '<li><a href="javascript:void(0)" onclick="loadConfigValuesForBot(\'' + currentBotName + '\')">' + currentBotName + '</a></li>';
            }

            $('.box-title').html('Currently editing: <b>' + botName + '</b>');
            $('#saveButton').data('BotName', botName);
            $('#botsDropDown').html(botsDropDownHTML);
        }
    });
}
//#endregion Config Page

//#region Layout
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

    function changeSetting() {
        swal({
            title: 'Are you sure?',
            text: 'Your IPC password will be reset!',
            type: 'warning',
            showCancelButton: true,
            confirmButtonClass: 'btn-danger',
            confirmButtonText: 'Yes, reset it!',
            closeOnConfirm: false
        }, function () {
            store('IPCPassword', '');
            swal({
                title: 'Success!',
                text: 'Your IPC password has been reset.',
                type: 'success'
            }, function () { location.reload(); });
        });
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

    function setup() {
        var tmpSkin = get('skin'),
            tmpLayoutState = get('layoutState'),
            tmpNightmodeState = get('nightmodeState'),
            tmpLeftSidebarState = get('leftSidebarState');

        if (tmpSkin && $.inArray(tmpSkin, mySkins)) changeSkin(tmpSkin);            
        if (tmpLeftSidebarState) {
            if (tmpLeftSidebarState === 'sidebar-collapse') {
                $('body').addClass('sidebar-collapse');
            }
        } 
        if (tmpLayoutState) changeBoxed(tmpLayoutState);
        if (tmpNightmodeState) changeNightmode(tmpNightmodeState);

        $('[data-skin]').on('click', function (e) { changeSkin($(this).data('skin')); });
        $('#toggleBoxed').on('click', function () { toggleBoxed(); });
        $('#toggleNightmode').on('click', function () { toggleNightmode(); });
        $('[data-general]').on('click', function () { changeSetting(); });
        $('#leftSidebar').on('click', function () {
            if ($('body').hasClass('sidebar-collapse')) {
                store('leftSidebarState', 'normal');
            } else {
                store('leftSidebarState', 'sidebar-collapse');
            }
        });
    }

    // Create the menu
    var $layoutSettings = $('<div />');

    // Layout options
    $layoutSettings.append(
        '<h4 class="control-sidebar-heading">'
        + 'General Settings'
        + '</h4>'
        // Reset IPC Password
        + '<div class="form-group">'
        + '<label class="control-sidebar-subheading">'
        + '<a href="javascript:void(0)" class="text-red pull-right" data-general="resetIPCPassword"><i class="far fa-trash-alt"></i></a>'
        + '<i class="fas fa-lock fa-fw"></i> Reset IPC Password'
        + '</label>'
        + '<p>Deletes the currently set IPC password</p>'
        + '</div>'
        // Boxed Layout
        + '<div class="form-group hidden-xs hidden-sm">'
        + '<label class="control-sidebar-subheading">'
        + '<button title="Toggle boxed layout" type="button" class="btn btn-box-tool pull-right text-grey" id="toggleBoxed"><i id="iconBoxed" class="fas fa-toggle-on fa-2x fa-rotate-180"></i></button>'
        + '<i class="far fa-square fa-fw"></i> Boxed Layout'
        + '</label>'
        + '<p>Toggle the boxed layout</p>'
        + '</div>'
        // Nightmode
        + '<div class="form-group">'
        + '<label class="control-sidebar-subheading">'
        + '<button title="Toggle nightmode" type="button" class="btn btn-box-tool pull-right text-grey" id="toggleNightmode"><i id="iconNightmode" class="fas fa-toggle-on fa-2x fa-rotate-180"></i></button>'
        + '<i class="fas fa-moon fa-fw"></i> Nightmode'
        + '</label>'
        + '<p>Toggle the nightmode</p>'
        + '</div>'
    );
    
    var $skinsList = $('<ul />', { 'class': 'list-unstyled clearfix' });
    
    var $skinBlue = $('<li />', { style: 'float:left; width: 14%; padding: 5px;' })
        .append('<a href="javascript:void(0)" data-skin="skin-blue" class="clearfix full-opacity-hover btn btn-badge bg-blue"></a>');
    $skinsList.append($skinBlue);
    var $skinBlack = $('<li />', { style: 'float:left; width: 14%; padding: 5px;' })
        .append('<a href="javascript:void(0)" data-skin="skin-black" class="clearfix full-opacity-hover btn btn-badge bg-black"></a>');
    $skinsList.append($skinBlack);
    var $skinPurple = $('<li />', { style: 'float:left; width: 14%; padding: 5px;' })
        .append('<a href="javascript:void(0)" data-skin="skin-purple" class="clearfix full-opacity-hover btn btn-badge bg-purple"></a>');
    $skinsList.append($skinPurple);
    var $skinGreen = $('<li />', { style: 'float:left; width: 14%; padding: 5px;' })
        .append('<a href="javascript:void(0)" data-skin="skin-green" class="clearfix full-opacity-hover btn btn-badge bg-green"></a>');
    $skinsList.append($skinGreen);
    var $skinRed = $('<li />', { style: 'float:left; width: 14%; padding: 5px;' })
        .append('<a href="javascript:void(0)" data-skin="skin-red" class="clearfix full-opacity-hover btn btn-badge bg-red"></a>');
    $skinsList.append($skinRed);
    var $skinYellow = $('<li />', { style: 'float:left; width: 14%; padding: 5px;' })
        .append('<a href="javascript:void(0)" data-skin="skin-yellow" class="clearfix full-opacity-hover btn btn-badge bg-yellow"></a>');
    $skinsList.append($skinYellow);
    var $skinTeal = $('<li />', { style: 'float:left; width: 14%; padding: 5px;' })
        .append('<a href="javascript:void(0)" data-skin="skin-teal" class="clearfix full-opacity-hover btn btn-badge bg-teal"></a>');
    $skinsList.append($skinTeal);

    var $skinsListLight = $('<ul />', { 'class': 'list-unstyled clearfix' });

    $layoutSettings.append('<h4 class="control-sidebar-heading">Skins</h4>');
    $layoutSettings.append($skinsList);

    $('#control-right-sidebar').after($layoutSettings);

    setup();
});
//#endregion Layout
