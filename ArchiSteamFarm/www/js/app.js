if (typeof jQuery === 'undefined') {
    throw new Error('ASF App requires jQuery')
}

function get(name) {
    if (typeof (Storage) !== 'undefined') {
        return localStorage.getItem(name)
    } else {
        window.alert('Please use a modern browser to properly view this template!')
    }
}

function store(name, val) {
    if (typeof (Storage) !== 'undefined') {
        localStorage.setItem(name, val)
    } else {
        window.alert('Please use a modern browser to properly view this template!')
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
            return false
        }

        store('IPCPassword', typedPassword);
        swal({
            title: "Nice!",
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

/*
* ASF Version in Footer
* ----------------------
*/
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
            var obj = data["Result"].Version
            var version = obj.Major + '.' + obj.Minor + '.' + obj.Build + '.' + obj.Revision;
            $("#version").html('<b>Version</b> ' + version);
        }
    });
});

/*
* Bot Status Buttons
* -------------------
*/
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
                    var obj = json[i];
                    var KeepRunning = obj.KeepRunning;
                    var TimeRemaining = obj.CardsFarmer.TimeRemaining;

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

/*
* ASF Information in left sidebar
* ------------------------
*/
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
    diff -= mins * (1000 * 60);

    return days + "d " + hours + "h " + mins + "m";
}

/*
* Command Page
* -------------
*/
var cmdInput = document.getElementById('commandInput');

function fillCommand(cmd) {
    cmdInput.value = cmd;
}

function fillBots(bot) {
    cmdInput.value = cmdInput.value + " " + bot;
}

function getDateAndTime() {
    var currentdate = new Date();
    return currentdate.getDate() + "."
        + (currentdate.getMonth() + 1) + "."
        + currentdate.getFullYear() + " @ "
        + currentdate.getHours() + ":"
        + currentdate.getMinutes() + ":"
        + currentdate.getSeconds();
}

function logCommand(state, cmd) {
    if (state) {
        $("#commandSent").val(getDateAndTime() + ' Command sent: ' + cmd);
    } else {
        $(".box-content-command").text(getDateAndTime() + ' Response received:' + cmd);
    }
}

function sendCommand() {
    if (cmdInput.value !== "") {
        logCommand(true, cmdInput.value);

        $.ajax({
            url: "/Api/Command/" + cmdInput.value,
            type: "GET",
            success: function (data) {
                logCommand(false, data['Result']);
            }
        });

        cmdInput.value = "";
    }
}

/*
* Layout
* -------
*/
$(function () {
    'use strict'

    $('[data-toggle="control-sidebar"]').controlSidebar()

    var $controlSidebar = $('[data-toggle="control-sidebar"]').data('lte.controlsidebar')

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
    ]

    function changeLayout(cls) {
        $('body').toggleClass(cls)
        $controlSidebar.fix()
    }

    function changeSkin(cls) {
        $.each(mySkins, function (i) {
            $('body').removeClass(mySkins[i])
        })

        $('body').addClass(cls)
        changeSidebarSkin(cls)
        store('skin', cls)
        return false
    }

    function changeSidebarSkin(cls) {
        var $sidebar = $('.control-sidebar')
        if (cls.includes("-light")) {
            if ($sidebar.hasClass('control-sidebar-dark')) {
                $sidebar.removeClass('control-sidebar-dark')
                $sidebar.addClass('control-sidebar-light')
            }
        } else {
            if ($sidebar.hasClass('control-sidebar-light')) {
                $sidebar.removeClass('control-sidebar-light')
                $sidebar.addClass('control-sidebar-dark')
            }
        }
    }

    function changeSetting(cls) {
        if (cls = "resetIPCPassword") {
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
                        title: "Nice!",
                        text: "Your IPC password has been resetted.",
                        type: "success"
                    }, function () {
                            location.reload();
                        })
                });
        }
    }

    function setup() {
        var tmpSkin = get('skin')
        if (tmpSkin && $.inArray(tmpSkin, mySkins)) {
            changeSkin(tmpSkin)
        }

        // Add the change skin listener
        $('[data-skin]').on('click', function (e) {
            if ($(this).hasClass('knob'))
                return
            e.preventDefault()
            changeSkin($(this).data('skin'))
        })

        // Add the layout manager
        $('[data-layout]').on('click', function () {
            changeLayout($(this).data('layout'))
        })

        // Add the general manager
        $('[data-general]').on('click', function () {
            changeSetting($(this).data('general'))
        })

        // Load sidebar state
        var tmpSidebarState = get('sidebarState')
        //console.log('sidebarstate read=' + tmpSidebarState)
        if (tmpSidebarState !== null && tmpSidebarState) {
            //console.log('sidebarstate loaded=' + tmpSidebarState)
            $controlSidebar.options.slide = !tmpSidebarState;

            if (!tmpSidebarState) {
                $('.control-sidebar').removeClass('control-sidebar-open')
            }

            if (tmpSidebarState) {
                $('[data-controlsidebar="control-sidebar-open"]').attr('checked', 'checked')
                //console.log('sidebarstate checked')
            }
            store('sidebarState', !tmpSidebarState)
        }

        $('[data-controlsidebar]').on('click', function () {
            changeLayout($(this).data('controlsidebar'))
            var slide = !$controlSidebar.options.slide
            $controlSidebar.options.slide = slide

            if (!slide) {
                $('.control-sidebar').removeClass('control-sidebar-open')
            }

            store('sidebarState', !slide)
            //console.log('sidebarstate stored=' + !slide)
        })

        //  Reset options
        if ($('body').hasClass('fixed')) {
            $('[data-layout="fixed"]').attr('checked', 'checked')
        }
        if ($('body').hasClass('layout-boxed')) {
            $('[data-layout="layout-boxed"]').attr('checked', 'checked')
        }
        if ($('body').hasClass('control-sidebar-open')) {
          $('[data-controlsidebar="control-sidebar-open"]').attr('checked', 'checked')
        }

    }

    // Create the layout tab
    var $tabPane = $('<div />', {
        'id': 'control-sidebar-layout-tab',
        'class': 'tab-pane active'
    })

    // Create the tab button
    var $tabButton = $('<li />', { 'class': 'active' })
        .html('<a href=\'#control-sidebar-layout-tab\' data-toggle=\'tab\'>'
        + '<i class="fa fa-wrench"></i>'
        + '</a>')

    // Add the tab button to the right sidebar tabs
    $('[href="#control-sidebar-info-tab"]')
        .parent()
        .before($tabButton)

    var $generalSettings = $('<div />')

    $generalSettings.append(
        '<h4 class="control-sidebar-heading">'
        + 'General Settings'
        + '</h4>'
        // Reset IPC Password
        + '<div class="form-group">'
        + '<label class="control-sidebar-subheading">'
        + '<a href="javascript:void(0)" class="text-red pull-right" data-general="resetIPCPassword"><i class="fa fa-trash-o"></i></a>'
        + 'Reset IPC Password'
        + '</label>'
        + '<p>Deletes the currently set IPC password</p>'
        + '</div>'
    )

    // Create the menu
    var $layoutSettings = $('<div />')

    // Layout options
    $layoutSettings.append(
        '<h4 class="control-sidebar-heading">'
        + 'Layout Options'
        + '</h4>'
        // Information
        + '<label class="control-sidebar-subheading">'
        + 'Information'
        + '</label>'
        + '<p>You can\'t use fixed and boxed layouts together</p>'
        + '</div>'
        // Fixed Layout
        + '<div class="form-group">'
        + '<label class="control-sidebar-subheading">'
        + '<input type="checkbox" data-layout="fixed"class="pull-right"/> '
        + 'Fixed Layout'
        + '</label>'
        + '<p>Activate the fixed layout</p>'
        + '</div>'
        // Boxed Layout
        + '<div class="form-group">'
        + '<label class="control-sidebar-subheading">'
        + '<input type="checkbox" data-layout="layout-boxed" class="pull-right"/> '
        + 'Boxed Layout'
        + '</label>'
        + '<p>Activate the boxed layout</p>'
        + '</div>'
        // Sidebar Slide
        + '<div class="form-group">'
        + '<label class="control-sidebar-subheading">'
        + '<input type="checkbox" data-controlsidebar="control-sidebar-open"class="pull-right"/> '
        + 'Sidebar Slide'
        + '</label>'
        + '<p>Toggle between slide over content and push content effects</p>'
        + '</div>'
    )
    var $skinsList = $('<ul />', { 'class': 'list-unstyled clearfix' })

    // Dark sidebar skins
    var $skinBlue =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-blue" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px; background: #367fa9"></span><span class="bg-light-blue" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #222d32"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin">Blue</p>')
    $skinsList.append($skinBlue)
    var $skinBlack =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-black" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div style="box-shadow: 0 0 2px rgba(0,0,0,0.1)" class="clearfix"><span style="display:block; width: 20%; float: left; height: 7px; background: #fefefe"></span><span style="display:block; width: 80%; float: left; height: 7px; background: #fefefe"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #222"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin">Black</p>')
    $skinsList.append($skinBlack)
    var $skinPurple =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-purple" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-purple-active"></span><span class="bg-purple" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #222d32"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin">Purple</p>')
    $skinsList.append($skinPurple)
    var $skinGreen =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-green" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-green-active"></span><span class="bg-green" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #222d32"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin">Green</p>')
    $skinsList.append($skinGreen)
    var $skinRed =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-red" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-red-active"></span><span class="bg-red" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #222d32"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin">Red</p>')
    $skinsList.append($skinRed)
    var $skinYellow =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-yellow" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-yellow-active"></span><span class="bg-yellow" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #222d32"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin">Yellow</p>')
    $skinsList.append($skinYellow)

    // Light sidebar skins
    var $skinBlueLight =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-blue-light" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px; background: #367fa9"></span><span class="bg-light-blue" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #f9fafc"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin" style="font-size: 12px">Blue Light</p>')
    $skinsList.append($skinBlueLight)
    var $skinBlackLight =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-black-light" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div style="box-shadow: 0 0 2px rgba(0,0,0,0.1)" class="clearfix"><span style="display:block; width: 20%; float: left; height: 7px; background: #fefefe"></span><span style="display:block; width: 80%; float: left; height: 7px; background: #fefefe"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #f9fafc"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin" style="font-size: 12px">Black Light</p>')
    $skinsList.append($skinBlackLight)
    var $skinPurpleLight =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-purple-light" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-purple-active"></span><span class="bg-purple" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #f9fafc"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin" style="font-size: 12px">Purple Light</p>')
    $skinsList.append($skinPurpleLight)
    var $skinGreenLight =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-green-light" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-green-active"></span><span class="bg-green" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #f9fafc"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin" style="font-size: 12px">Green Light</p>')
    $skinsList.append($skinGreenLight)
    var $skinRedLight =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-red-light" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-red-active"></span><span class="bg-red" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #f9fafc"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin" style="font-size: 12px">Red Light</p>')
    $skinsList.append($skinRedLight)
    var $skinYellowLight =
        $('<li />', { style: 'float:left; width: 33.33333%; padding: 5px;' })
            .append('<a href="javascript:void(0)" data-skin="skin-yellow-light" style="display: block; box-shadow: 0 0 3px rgba(0,0,0,0.4)" class="clearfix full-opacity-hover">'
            + '<div><span style="display:block; width: 20%; float: left; height: 7px;" class="bg-yellow-active"></span><span class="bg-yellow" style="display:block; width: 80%; float: left; height: 7px;"></span></div>'
            + '<div><span style="display:block; width: 20%; float: left; height: 20px; background: #f9fafc"></span><span style="display:block; width: 80%; float: left; height: 20px; background: #f4f5f7"></span></div>'
            + '</a>'
            + '<p class="text-center no-margin" style="font-size: 12px">Yellow Light</p>')
    $skinsList.append($skinYellowLight)

    $layoutSettings.append('<h4 class="control-sidebar-heading">Skins</h4>')
    $layoutSettings.append($skinsList)

    $tabPane.append($generalSettings)

    $tabPane.append($layoutSettings)
    $('#control-sidebar-info-tab').after($tabPane)

    setup()

    $('[data-toggle="tooltip"]').tooltip()
})