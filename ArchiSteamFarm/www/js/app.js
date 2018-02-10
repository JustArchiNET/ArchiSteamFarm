//#region Utils
if (typeof jQuery === 'undefined') {
    throw new Error('ASF App requires jQuery');
}

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
        title: "IPC password required",
        text: "Please enter the correct IPC password:",
        type: "input",
        showCancelButton: true,
        closeOnConfirm: false,
        inputPlaceholder: "Type your password",
        inputType: "password"
    }, function (typedPassword) {
        if (typedPassword === false) return false;

        if (typedPassword === "") {
            swal.showInputError("You need to enter a valid password!");
            return false;
        }

        store('IPCPassword', typedPassword);
        swal({
            title: "Success!",
            text: "Your IPC password has been saved.",
            type: "success"
        }, function () {
            location.reload();
        });
    });
}

var IPCPassword = get('IPCPassword');

if (IPCPassword) {
    $.ajaxSetup({
        beforeSend: function (jqXHR) {
            jqXHR.setRequestHeader('Authentication', IPCPassword);
        }
    });
}
//#endregion Utils

//#region Footer
$('.main-footer').ready(function () {
    $.ajax({
        url: "/Api/ASF",
        type: "GET",
        statusCode: {
            401: function () {
                getIPCPassword();
            }
        },
        success: function (data) {
            var obj = data["Result"].Version,
                version = obj.Major + '.' + obj.Minor + '.' + obj.Build + '.' + obj.Revision;

            // Add version to footer
            $("#version").html('<b>Version</b> ' + version);

            // Change changelog link according to currently running version
            document.getElementById("changelog").href = "https://github.com/JustArchi/ArchiSteamFarm/releases/tag/" + version;
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
            url: "/Api/Bot/ASF",
            type: "GET",
            success: function (data) {
                var json = data["Result"];

                for (var i = 0; i < json.length; i++) {
                    var obj = json[i],
                        KeepRunning = obj.KeepRunning,
                        TimeRemaining = obj.CardsFarmer.TimeRemaining;

                    if (KeepRunning === false) {
                        offlineBots++;
                        $("#offlineBots").text(offlineBots);
                    } else {
                        if (TimeRemaining === "00:00:00") {
                            idleBots++;
                            $("#idleBots").text(idleBots);
                        } else {
                            activeBots++;
                            $("#activeBots").text(activeBots);
                        }
                    }
                }
            }
        });
    }

    displayBotStatus();

    window.setInterval(function () {
        displayBotStatus();
    }, 5000);
});
//#endregion Bot Status Buttons

//#region ASF Information
$('.info-overview').ready(function () {
    // Display RAM usage
    function displayRAMUsage() {
        $.ajax({
            url: "/Api/ASF",
            type: "GET",
            success: function (data) {
                $("#ramUsage").html((data["Result"].MemoryUsage / 1024).toFixed(2) + " MB");
            }
        });
    }

    displayRAMUsage();

    window.setInterval(function () {
        displayRAMUsage();
    }, 10000);

    // Display uptime
    function displayUptime() {
        $.ajax({
            url: "/Api/ASF",
            type: "GET",
            success: function (data) {
                $("#uptime").html(uptimeToString(data["Result"].ProcessStartTime));
            }
        });
    }

    displayUptime();

    window.setInterval(function () {
        displayUptime();
    }, 60000);
});

function uptimeToString(startTime) {
    var processStartTime = new Date(startTime);
    var currentDate = new Date();

    var diff = currentDate.getTime() - processStartTime.getTime();

    var days = Math.floor(diff / (1000 * 60 * 60 * 24));
    diff -= days * (1000 * 60 * 60 * 24);

    var hours = Math.floor(diff / (1000 * 60 * 60));
    diff -= hours * (1000 * 60 * 60);

    var mins = Math.floor(diff / (1000 * 60));

    hours = (hours < 10 ? '0' : '') + hours;
    mins = (mins < 10 ? '0' : '') + mins;

    return days + "d " + hours + "h " + mins + "m";
}
//#endregion ASF Information

//#region Command Page
var cmdInput = document.getElementById('commandInput');

function fillCommand(cmd) {
    cmdInput.value = cmd;
}

function fillBots(bot) {
    cmdInput.value = cmdInput.value + " " + bot;
}

function getDateAndTime() {
    var currentdate = new Date();
    return ('0' + currentdate.getDate()).slice(-2) + '.'
        + ('0' + (currentdate.getMonth() + 1)).slice(-2) + '.'
        + currentdate.getFullYear() + " @ "
        + ('0' + currentdate.getHours()).slice(-2) + ":"
        + ('0' + currentdate.getMinutes()).slice(-2) + ":"
        + ('0' + currentdate.getSeconds()).slice(-2);
}

function logCommand(state, cmd) {
    var tmpAutoClear = get('autoClear');

    if (state) {
        $("#commandSent").val(getDateAndTime() + ' Command sent: ' + cmd);
    } else {
        if (tmpAutoClear === 'false') {
            $(".box-content-command").append('\n' + getDateAndTime() + ' Response received: ' + cmd + '\n');
        } else {
            $(".box-content-command").text(getDateAndTime() + ' Response received: ' + cmd);
        }
    }
}

function sendCommand() {
    var command = cmdInput.value,
        requestURL = "/Api/Command/" + command,
        tmpAutoClear = get('autoClear');

    if (command === "") {
        return;
    }

    logCommand(true, command);

    if (tmpAutoClear === 'false') {
        if ($(".box-content-command").text() === '') {
            $(".box-content-command").append(getDateAndTime() + ' Waiting for response...' + '\n');
        } else {
            $(".box-content-command").append('\n' + getDateAndTime() + ' Waiting for response...' + '\n');
        }

    } else {
        $(".box-content-command").text(getDateAndTime() + ' Waiting for response...');
    }

    $("#commandReply").append('<div class="overlay"><i class="fas fa-sync fa-spin" style="color:white"></i></div>');

    $.ajax({
        url: requestURL,
        type: "GET",
        success: function (data) {
            $('.overlay').remove();
            logCommand(false, data['Result']);
        },
        error: function (jqXHR, textStatus, errorThrown) {
            $('.overlay').remove();
            logCommand(false, jqXHR.status + ' - ' + errorThrown);
        }
    });

    if (tmpAutoClear !== 'false') {
        cmdInput.value = "";
    }
}
//#endregion Command Page

//#region Config Changer Page
var infoMessageHTML = '<div class="callout callout-warning margin">'
    + '<h4><i class="icon fas fa-exclamation-triangle"></i> Under development</h4>'
    + '<p>This feature is currently being developed.</p>'
    + '</div>';

function generateConfigChangerHTML() {
    $.ajax({
        url: "/Api/Type/ArchiSteamFarm.BotConfig",
        type: "GET",
        success: function (data) {
            var obj = data["Result"];
            var boxBodyHTML = "";
            var textBoxes = '';
            var checkBoxes = '';
            var numberBoxes = '';
            var defaultBoxes = '';
            var textAreas = '';

            //console.log(obj)

            for (var key in obj) {
                if (obj.hasOwnProperty(key)) {
                    var value = obj[key];
                    var keyOne = key.replace(/([A-Z])/g, ' $1').trim();
                    var keyWithSpace = keyOne.replace(/([A-Z])\s(?=[A-Z])/g, '$1');

                    switch (value) {
                        case 'System.Boolean':
                            // Add checkbox
                            checkBoxes += '<div class="checkbox">'
                                + '<label for="' + key + '">'
                                + '<input type="checkbox" id="' + key + '" data-type="' + value + '">'
                                + keyWithSpace
                                + '</label>'
                                + '</div>';
                            break;
                        case 'System.Byte':
                            // Add textbox
                            numberBoxes += '<div class="form-group">'
                                + '<label for="' + key + '">' + keyWithSpace + '</label>'
                                + '<input type="number" id="' + key + '" class="form-control" data-type="' + value + '">'
                                + '</div>';
                            break;
                        case 'System.String':
                            // Add textbox
                            textBoxes += '<div class="form-group">'
                                + '<label for="' + key + '">' + keyWithSpace + '</label>'
                                + '<input type="text" id="' + key + '" class="form-control" data-type="' + value + '">'
                                + '</div>';
                            break;
                        case 'System.Collections.Generic.Dictionary`2[System.UInt64][ArchiSteamFarm.BotConfig+EPermission]':
                            // Add textarea
                            textAreas += '<div class="form-group">'
                                + '<label for="' + key + '">' + keyWithSpace + '</label>'
                                + '<textarea id="' + key + '" class="form-control" data-type="' + value + '" rows="3"></textarea>'
                                + '</div>';
                            break;
                        default:
                            // Default use textbox
                            defaultBoxes += '<div class="form-group">'
                                + '<label for="' + key + '">' + keyWithSpace + '</label>'
                                + '<input type="text" id="' + key + '" class="form-control" data-type="' + value + '">'
                                + '</div>';
                    }
                }

                boxBodyHTML = '<div class="col-lg-4 col-md-4 col-sm-6 col-xs-12">' + defaultBoxes + '</div>'
                    + '<div class="col-lg-4 col-md-4 col-sm-6 col-xs-12">' + textBoxes + numberBoxes + '</div>'
                    + '<div class="col-lg-4 col-md-4 col-sm-6 col-xs-12">' + checkBoxes + textAreas + '</div>';
            }

            $('#configChangerTab').html(infoMessageHTML
                + '<div class="box-header with-border">'
                + '<h3 class="box-title"></h3>'
                + '<div class="box-tools pull-right">'
                + '<div class="btn-group">'
                + '<button type="button" class="btn btn-box-tool dropdown-toggle" data-toggle="dropdown" aria-expanded="false">'
                + 'Change Bot '
                + '<span class="fas fa-caret-down"></span>'
                + '</button>'
                + '<ul class="dropdown-menu scrollable-menu" role="menu" id="botsDropDown"></ul>'
                + '</div>'
                + '</div>'
                + '</div>'
                + '<div class="box-body">'
                + boxBodyHTML
                + '</div>');
        }
    });
}

function loadConfigValuesForBot(botName) {
    $.ajax({
        url: "/Api/Bot/" + encodeURIComponent(botName),
        type: "GET",
        success: function (data) {
            var obj = data["Result"];
            var objBot = obj[0];
            var BotConfig = objBot.BotConfig;
            
            //console.log(BotConfig)

            for (var key in BotConfig) {
                if (BotConfig.hasOwnProperty(key)) {
                    var value = BotConfig[key];

                    var $key = $('#' + key);
                    var keyObj = $key[0];
                    var inputType = keyObj.type;

                    //console.log(key + ' - ' + inputType)

                    switch (inputType) {
                        case 'checkbox':
                            $key.prop('checked', value);
                            break;
                        case 'textarea':
                            $key.text(''); // Reset textarea before filling

                            for (var steamID64 in value) {
                                if (value.hasOwnProperty(steamID64)) {
                                    var permission = value[steamID64];
                                    $key.append(steamID64 + ':' + permission + '\n');
                                }
                            }
                            break;
                        default:
                            $key.val(value);
                    }
                }
            }
            //setDefaultValues();

            loadBotsDropDown(botName);
        }
    });
}

function loadBotsDropDown(botName) {
    var botsDropDownHTML = '';

    $.ajax({
        url: "/Api/Bot/ASF",
        type: "GET",
        success: function (data) {
            var obj = data["Result"];

            for (var i = 0; i < obj.length; i++) {
                var currentBot = obj[i],
                    currentBotName = currentBot.BotName;

                if (botName !== currentBotName) {
                    botsDropDownHTML += '<li><a href="#" onclick="loadConfigValuesForBot(\'' + currentBotName + '\')">' + currentBotName + '</a></li>';
                }
            }

            $(".box-title").html("Currently editing: <b>" + botName + "</b>");
            $("#saveConfig").data("BotName", botName);
            $("#botsDropDown").html(botsDropDownHTML);
        }
    });
}
//#endregion Config Changer Page

//#region Layout
$(function () {
    'use strict';

    var mySkins = [
        'skin-blue',
        'skin-black',
        'skin-red',
        'skin-yellow',
        'skin-purple',
        'skin-green',
        'skin-blue-light',
        'skin-black-light',
        'skin-red-light',
        'skin-yellow-light',
        'skin-purple-light',
        'skin-green-light'
    ];

    function changeSkin(cls) {
        $.each(mySkins, function (i) {
            $('body').removeClass(mySkins[i]);
        });

        $('body').addClass(cls);
        changeSidebarSkin(cls);
        store('skin', cls);
        return false;
    }

    function changeSidebarSkin(cls) {
        var $sidebar = $('.control-sidebar');
        if (cls.includes("-light")) {
            if ($sidebar.hasClass('control-sidebar-dark')) {
                $sidebar.removeClass('control-sidebar-dark');
                $sidebar.addClass('control-sidebar-light');
            }
        } else {
            if ($sidebar.hasClass('control-sidebar-light')) {
                $sidebar.removeClass('control-sidebar-light');
                $sidebar.addClass('control-sidebar-dark');
            }
        }
    }

    function changeSetting() {
        swal({
            title: "Are you sure?",
            text: "Your IPC password will be reset!",
            type: "warning",
            showCancelButton: true,
            confirmButtonClass: "btn-danger",
            confirmButtonText: "Yes, reset it!",
            closeOnConfirm: false
        }, function () {
            store('IPCPassword', "");
            swal({
                title: "Success!",
                text: "Your IPC password has been reset.",
                type: "success"
            }, function () {
                location.reload();
            });
        });
    }

    function changeBoxed(savedLayout) {
        if (savedLayout === 'layout-boxed') {
            if ($('body').hasClass('fixed')) {
                $('body').removeClass('fixed');
                $('body').addClass('layout-boxed');
            }
        }
    }

    function toggleBoxed() {
        if ($('body').hasClass('fixed')) {
            $('body').removeClass('fixed');
            $('body').addClass('layout-boxed');
            store('layoutState', 'layout-boxed');
        } else {
            $('body').removeClass('layout-boxed');
            $('body').addClass('fixed');
            store('layoutState', 'fixed');
        }
    }

    function saveLeftSidebarState() {
        if ($('body').hasClass('sidebar-collapse')) {
            store('leftSidebarState', 'normal');
        } else {
            store('leftSidebarState', 'sidebar-collapse');
        }
    }

    function changeLeftSidebarState(savedSidebarState) {
        if (savedSidebarState === 'sidebar-collapse') {
            $('body').addClass('sidebar-collapse');
        }
    }

    function setup() {
        var tmpSkin = get('skin'),
            tmpLayoutState = get('layoutState'),
            tmpLeftSidebarState = get('leftSidebarState');

        if (tmpSkin && $.inArray(tmpSkin, mySkins)) {
            changeSkin(tmpSkin);
        }

        if (tmpLeftSidebarState) {
            changeLeftSidebarState(tmpLeftSidebarState);
        }

        if (tmpLayoutState) {
            changeBoxed(tmpLayoutState);
        }

        $('[data-skin]').on('click', function (e) {
            changeSkin($(this).data('skin'));
        });

        $('[data-layout]').on('click', function () {
            toggleBoxed();
        });

        $('[data-general]').on('click', function () {
            changeSetting();
        });

        $('[data-navigation]').on('click', function () {
            saveLeftSidebarState();
        });

        if ($('body').hasClass('layout-boxed')) {
            $('[data-layout="layout-boxed"]').attr('checked', 'checked');
        }
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
        + 'Reset IPC Password'
        + '</label>'
        + '<p>Deletes the currently set IPC password</p>'
        + '</div>'
        // Boxed Layout
        + '<div class="form-group hidden-xs hidden-sm">'
        + '<label class="control-sidebar-subheading">'
        + '<input type="checkbox" data-layout="layout-boxed" class="pull-right"/> '
        + 'Boxed Layout'
        + '</label>'
        + '<p>Activate the boxed layout</p>'
        + '</div>'
    );

    //#region SkinsList
    var $skinsList = $('<ul />', { 'class': 'list-unstyled clearfix' });

    // Dark sidebar skins
    var $skinBlue =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-blue" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px; background: #367fa9"></span><span class="bg-light-blue" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #222d32"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin">Blue</p>');
    $skinsList.append($skinBlue);
    var $skinBlack =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-black" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div style="box-shadow: 0 0 2px rgba(0,0,0,0.1)" class="clearfix"><span style="display:block; width: 20%; float: left; height: 7px; background: #fefefe"></span><span style="display:block; width: 80%; float: left; height: 7px; background: #fefefe"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #222"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin">Black</p>');
    $skinsList.append($skinBlack);
    var $skinPurple =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-purple" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-purple-active"></span><span class="bg-purple" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #222d32"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin">Purple</p>');
    $skinsList.append($skinPurple);
    var $skinGreen =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-green" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-green-active"></span><span class="bg-green" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #222d32"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin">Green</p>');
    $skinsList.append($skinGreen);
    var $skinRed =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-red" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-red-active"></span><span class="bg-red" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #222d32"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin">Red</p>');
    $skinsList.append($skinRed);
    var $skinYellow =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-yellow" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-yellow-active"></span><span class="bg-yellow" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #222d32"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin">Yellow</p>');
    $skinsList.append($skinYellow);

    // Light sidebar skins
    var $skinBlueLight =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-blue-light" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px; background: #367fa9"></span><span class="bg-light-blue" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #f9fafc"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin" style="font-size: 12px">Blue Light</p>');
    $skinsList.append($skinBlueLight);
    var $skinBlackLight =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-black-light" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div style="box-shadow: 0 0 2px rgba(0,0,0,0.1)" class="clearfix"><span style="display:block; width: 20%; float: left; height: 7px; background: #fefefe"></span><span style="display:block; width: 80%; float: left; height: 7px; background: #fefefe"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #f9fafc"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin" style="font-size: 12px">Black Light</p>');
    $skinsList.append($skinBlackLight);
    var $skinPurpleLight =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-purple-light" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-purple-active"></span><span class="bg-purple" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #f9fafc"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin" style="font-size: 12px">Purple Light</p>');
    $skinsList.append($skinPurpleLight);
    var $skinGreenLight =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-green-light" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-green-active"></span><span class="bg-green" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #f9fafc"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin" style="font-size: 12px">Green Light</p>');
    $skinsList.append($skinGreenLight);
    var $skinRedLight =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-red-light" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-red-active"></span><span class="bg-red" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #f9fafc"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin" style="font-size: 12px">Red Light</p>');
    $skinsList.append($skinRedLight);
    var $skinYellowLight =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-yellow-light" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-yellow-active"></span><span class="bg-yellow" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #f9fafc"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin" style="font-size: 12px">Yellow Light</p>');
    $skinsList.append($skinYellowLight);
    //#endregion SkinsList

    $layoutSettings.append('<h4 class="control-sidebar-heading">Skins</h4>');
    $layoutSettings.append($skinsList);

    $('#control-right-sidebar').after($layoutSettings);

    setup();
});
//#endregion Layout
