if (typeof jQuery === 'undefined') {
    throw new Error('ASF App requires jQuery')
}

'use strict';

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
    //make this more beautiful
    IPCPassword = prompt("Please enter your IPC password:");
    if (IPCPassword !== null || IPCPassword !== "") {
        store('IPCPassword', IPCPassword);
        location.reload();
    }
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
            var objVersion = data["Result"].Version
            var Major = objVersion.Major;
            var Minor = objVersion.Minor;
            var Build = objVersion.Build
            var Revision = objVersion.Revision;
            var version = Major + '.' + Minor + '.' + Build + '.' + Revision;

            $("#version").html('<b>Version</b> ' + version);
        }
    });
});

/*
* Bot Status Buttons
* -------------------
*/
var activeBots = 0;
var idleBots = 0;
var offlineBots = 0;

$.ajax({
    url: "/Api/Bot/ASF",
    type: "GET",
    success: function (data) {
        var json = data["Result"];

        for (var i = 0; i < json.length; i++) {
            var obj = json[i];
            var SteamID = obj.SteamID;
            var KeepRunning = obj.KeepRunning;
            var TimeRemaining = obj.CardsFarmer.TimeRemaining;

            if (SteamID === 0 && KeepRunning === false) {
                offlineBots++;
                $("#offlineBots").text(offlineBots);
            }

            if (SteamID !== 0 && KeepRunning === true && TimeRemaining === "00:00:00") {
                idleBots++;
                $("#idleBots").text(idleBots);
            }

            if (SteamID !== 0 && KeepRunning === true && TimeRemaining !== "00:00:00") {
                activeBots++;
                $("#activeBots").text(activeBots);
            }
        }
    }
});


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

function logCommand(state, cmd) {
    var currentdate = new Date();
    var datetime = currentdate.getDate() + "."
        + (currentdate.getMonth() + 1) + "."
        + currentdate.getFullYear() + " "
        + currentdate.getHours() + ":"
        + currentdate.getMinutes() + ":"
        + currentdate.getSeconds();

    if (state) {
        $("#commandReply").text(datetime + "|GUI|INFO|ASF|Sent IPC command: " + cmd);
    } else {
        $("#commandReply").text($("#commandReply").text() + "\n" + datetime + "|GUI|INFO|ASF|Answered to IPC command: " + cmd);
    }
}

function sendCommand() {
    $.ajax({
        url: "/Api/Command/" + cmdInput.value,
        type: "GET",
        success: function (data) {
            logCommand(false, data['Result']);
        }
    });

    logCommand(true, cmdInput.value);
    cmdInput.value = "";
}

/* Tree()
 * ======
 * Converts a nested list into a multilevel
 * tree view menu.
 *
 * @Usage: $('.my-menu').tree(options)
 *         or add [data-widget="tree"] to the ul element
 *         Pass any option as data-option="value"
 */
+function ($) {
    'use strict'

    var DataKey = 'lte.tree'

    var Default = {
        animationSpeed: 500,
        accordion: true,
        followLink: false,
        trigger: '.treeview a'
    }

    var Selector = {
        tree: '.tree',
        treeview: '.treeview',
        treeviewMenu: '.treeview-menu',
        open: '.menu-open, .active',
        li: 'li',
        data: '[data-widget="tree"]',
        active: '.active'
    }

    var ClassName = {
        open: 'menu-open',
        tree: 'tree'
    }

    var Event = {
        collapsed: 'collapsed.tree',
        expanded: 'expanded.tree'
    }

    // Tree Class Definition
    // =====================
    var Tree = function (element, options) {
        this.element = element
        this.options = options

        $(this.element).addClass(ClassName.tree)

        $(Selector.treeview + Selector.active, this.element).addClass(ClassName.open)

        this._setUpListeners()
    }

    Tree.prototype.toggle = function (link, event) {
        var treeviewMenu = link.next(Selector.treeviewMenu)
        var parentLi = link.parent()
        var isOpen = parentLi.hasClass(ClassName.open)

        if (!parentLi.is(Selector.treeview)) {
            return
        }

        if (!this.options.followLink || link.attr('href') === '#') {
            event.preventDefault()
        }

        if (isOpen) {
            this.collapse(treeviewMenu, parentLi)
        } else {
            this.expand(treeviewMenu, parentLi)
        }
    }

    Tree.prototype.expand = function (tree, parent) {
        var expandedEvent = $.Event(Event.expanded)

        if (this.options.accordion) {
            var openMenuLi = parent.siblings(Selector.open)
            var openTree = openMenuLi.children(Selector.treeviewMenu)
            this.collapse(openTree, openMenuLi)
        }

        parent.addClass(ClassName.open)
        tree.slideDown(this.options.animationSpeed, function () {
            $(this.element).trigger(expandedEvent)
        }.bind(this))
    }

    Tree.prototype.collapse = function (tree, parentLi) {
        var collapsedEvent = $.Event(Event.collapsed)

        tree.find(Selector.open).removeClass(ClassName.open)
        parentLi.removeClass(ClassName.open)
        tree.slideUp(this.options.animationSpeed, function () {
            tree.find(Selector.open + ' > ' + Selector.treeview).slideUp()
            $(this.element).trigger(collapsedEvent)
        }.bind(this))
    }

    // Private

    Tree.prototype._setUpListeners = function () {
        var that = this

        $(this.element).on('click', this.options.trigger, function (event) {
            that.toggle($(this), event)
        })
    }

    // Plugin Definition
    // =================
    function Plugin(option) {
        return this.each(function () {
            var $this = $(this)
            var data = $this.data(DataKey)

            if (!data) {
                var options = $.extend({}, Default, $this.data(), typeof option == 'object' && option)
                $this.data(DataKey, new Tree($this, options))
            }
        })
    }

    var old = $.fn.tree

    $.fn.tree = Plugin
    $.fn.tree.Constructor = Tree

    // No Conflict Mode
    // ================
    $.fn.tree.noConflict = function () {
        $.fn.tree = old
        return this
    }

    // Tree Data API
    // =============
    $(window).on('load', function () {
        $(Selector.data).each(function () {
            Plugin.call($(this))
        })
    })

}(jQuery)


    /* BoxRefresh()
     * =========
     * Adds AJAX content control to a box.
     *
     * @Usage: $('#my-box').boxRefresh(options)
     *         or add [data-widget="box-refresh"] to the box element
     *         Pass any option as data-option="value"
     */
    + function ($) {
        'use strict'

        var DataKey = 'lte.boxrefresh'

        var Default = {
            source: '',
            params: {},
            trigger: '.refresh-btn',
            content: '.box-body',
            loadInContent: true,
            responseType: '',
            overlayTemplate: '<div class="overlay"><div class="fa fa-refresh fa-spin"></div></div>',
            onLoadStart: function () {
            },
            onLoadDone: function (response) {
                return response
            }
        }

        var Selector = {
            data: '[data-widget="box-refresh"]'
        }

        // BoxRefresh Class Definition
        // =========================
        var BoxRefresh = function (element, options) {
            this.element = element
            this.options = options
            this.$overlay = $(options.overlay)

            if (options.source === '') {
                throw new Error('Source url was not defined. Please specify a url in your BoxRefresh source option.')
            }

            this._setUpListeners()
            this.load()
        }

        BoxRefresh.prototype.load = function () {
            this._addOverlay()
            this.options.onLoadStart.call($(this))

            $.get(this.options.source, this.options.params, function (response) {
                if (this.options.loadInContent) {
                    $(this.options.content).html(response)
                }
                this.options.onLoadDone.call($(this), response)
                this._removeOverlay()
            }.bind(this), this.options.responseType !== '' && this.options.responseType)
        }

        // Private

        BoxRefresh.prototype._setUpListeners = function () {
            $(this.element).on('click', Selector.trigger, function (event) {
                if (event) event.preventDefault()
                this.load()
            }.bind(this))
        }

        BoxRefresh.prototype._addOverlay = function () {
            $(this.element).append(this.$overlay)
        }

        BoxRefresh.prototype._removeOverlay = function () {
            $(this.element).remove(this.$overlay)
        }

        // Plugin Definition
        // =================
        function Plugin(option) {
            return this.each(function () {
                var $this = $(this)
                var data = $this.data(DataKey)

                if (!data) {
                    var options = $.extend({}, Default, $this.data(), typeof option == 'object' && option)
                    $this.data(DataKey, (data = new BoxRefresh($this, options)))
                }

                if (typeof data == 'string') {
                    if (typeof data[option] == 'undefined') {
                        throw new Error('No method named ' + option)
                    }
                    data[option]()
                }
            })
        }

        var old = $.fn.boxRefresh

        $.fn.boxRefresh = Plugin
        $.fn.boxRefresh.Constructor = BoxRefresh

        // No Conflict Mode
        // ================
        $.fn.boxRefresh.noConflict = function () {
            $.fn.boxRefresh = old
            return this
        }

        // BoxRefresh Data API
        // =================
        $(window).on('load', function () {
            $(Selector.data).each(function () {
                Plugin.call($(this))
            })
        })

    }(jQuery)


    /* BoxWidget()
     * ======
     * Adds box widget functions to boxes.
     *
     * @Usage: $('.my-box').boxWidget(options)
     *         This plugin auto activates on any element using the `.box` class
     *         Pass any option as data-option="value"
     */
    + function ($) {
        'use strict'

        var DataKey = 'lte.boxwidget'

        var Default = {
            animationSpeed: 500,
            collapseTrigger: '[data-widget="collapse"]',
            removeTrigger: '[data-widget="remove"]',
            collapseIcon: 'fa-minus',
            expandIcon: 'fa-plus',
            removeIcon: 'fa-times'
        }

        var Selector = {
            data: '.box',
            collapsed: '.collapsed-box',
            body: '.box-body',
            footer: '.box-footer',
            tools: '.box-tools'
        }

        var ClassName = {
            collapsed: 'collapsed-box'
        }

        var Event = {
            collapsed: 'collapsed.boxwidget',
            expanded: 'expanded.boxwidget',
            removed: 'removed.boxwidget'
        }

        // BoxWidget Class Definition
        // =====================
        var BoxWidget = function (element, options) {
            this.element = element
            this.options = options

            this._setUpListeners()
        }

        BoxWidget.prototype.toggle = function () {
            var isOpen = !$(this.element).is(Selector.collapsed)

            if (isOpen) {
                this.collapse()
            } else {
                this.expand()
            }
        }

        BoxWidget.prototype.expand = function () {
            var expandedEvent = $.Event(Event.expanded)
            var collapseIcon = this.options.collapseIcon
            var expandIcon = this.options.expandIcon

            $(this.element).removeClass(ClassName.collapsed)

            $(this.element)
                .find(Selector.tools)
                .find('.' + expandIcon)
                .removeClass(expandIcon)
                .addClass(collapseIcon)

            $(this.element).find(Selector.body + ', ' + Selector.footer)
                .slideDown(this.options.animationSpeed, function () {
                    $(this.element).trigger(expandedEvent)
                }.bind(this))
        }

        BoxWidget.prototype.collapse = function () {
            var collapsedEvent = $.Event(Event.collapsed)
            var collapseIcon = this.options.collapseIcon
            var expandIcon = this.options.expandIcon

            $(this.element)
                .find(Selector.tools)
                .find('.' + collapseIcon)
                .removeClass(collapseIcon)
                .addClass(expandIcon)

            $(this.element).find(Selector.body + ', ' + Selector.footer)
                .slideUp(this.options.animationSpeed, function () {
                    $(this.element).addClass(ClassName.collapsed)
                    $(this.element).trigger(collapsedEvent)
                }.bind(this))
        }

        BoxWidget.prototype.remove = function () {
            var removedEvent = $.Event(Event.removed)

            $(this.element).slideUp(this.options.animationSpeed, function () {
                $(this.element).trigger(removedEvent)
                $(this.element).remove()
            }.bind(this))
        }

        // Private

        BoxWidget.prototype._setUpListeners = function () {
            var that = this

            $(this.element).on('click', this.options.collapseTrigger, function (event) {
                if (event) event.preventDefault()
                that.toggle()
            })

            $(this.element).on('click', this.options.removeTrigger, function (event) {
                if (event) event.preventDefault()
                that.remove()
            })
        }

        // Plugin Definition
        // =================
        function Plugin(option) {
            return this.each(function () {
                var $this = $(this)
                var data = $this.data(DataKey)

                if (!data) {
                    var options = $.extend({}, Default, $this.data(), typeof option == 'object' && option)
                    $this.data(DataKey, (data = new BoxWidget($this, options)))
                }

                if (typeof option == 'string') {
                    if (typeof data[option] == 'undefined') {
                        throw new Error('No method named ' + option)
                    }
                    data[option]()
                }
            })
        }

        var old = $.fn.boxWidget

        $.fn.boxWidget = Plugin
        $.fn.boxWidget.Constructor = BoxWidget

        // No Conflict Mode
        // ================
        $.fn.boxWidget.noConflict = function () {
            $.fn.boxWidget = old
            return this
        }

        // BoxWidget Data API
        // ==================
        $(window).on('load', function () {
            $(Selector.data).each(function () {
                Plugin.call($(this))
            })
        })

    }(jQuery)


    /* ControlSidebar()
     * ===============
     * Toggles the state of the control sidebar
     *
     * @Usage: $('#control-sidebar-trigger').controlSidebar(options)
     *         or add [data-toggle="control-sidebar"] to the trigger
     *         Pass any option as data-option="value"
     */
    + function ($) {
        'use strict'

        var DataKey = 'lte.controlsidebar'

        var Default = {
            slide: true
        }

        var Selector = {
            sidebar: '.control-sidebar',
            data: '[data-toggle="control-sidebar"]',
            open: '.control-sidebar-open',
            bg: '.control-sidebar-bg',
            wrapper: '.wrapper',
            content: '.content-wrapper',
            boxed: '.layout-boxed'
        }

        var ClassName = {
            open: 'control-sidebar-open',
            fixed: 'fixed'
        }

        var Event = {
            collapsed: 'collapsed.controlsidebar',
            expanded: 'expanded.controlsidebar'
        }

        // ControlSidebar Class Definition
        // ===============================
        var ControlSidebar = function (element, options) {
            this.element = element
            this.options = options
            this.hasBindedResize = false

            this.init()
        }

        ControlSidebar.prototype.init = function () {
            // Add click listener if the element hasn't been
            // initialized using the data API
            if (!$(this.element).is(Selector.data)) {
                $(this).on('click', this.toggle)
            }

            this.fix()
            $(window).resize(function () {
                this.fix()
            }.bind(this))
        }

        ControlSidebar.prototype.toggle = function (event) {
            if (event) event.preventDefault()

            this.fix()

            if (!$(Selector.sidebar).is(Selector.open) && !$('body').is(Selector.open)) {
                this.expand()
            } else {
                this.collapse()
            }
        }

        ControlSidebar.prototype.expand = function () {
            if (!this.options.slide) {
                $('body').addClass(ClassName.open)
            } else {
                $(Selector.sidebar).addClass(ClassName.open)
            }

            $(this.element).trigger($.Event(Event.expanded))
        }

        ControlSidebar.prototype.collapse = function () {
            $('body, ' + Selector.sidebar).removeClass(ClassName.open)
            $(this.element).trigger($.Event(Event.collapsed))
        }

        ControlSidebar.prototype.fix = function () {
            if ($('body').is(Selector.boxed)) {
                this._fixForBoxed($(Selector.bg))
            }
        }

        // Private

        ControlSidebar.prototype._fixForBoxed = function (bg) {
            bg.css({
                position: 'absolute',
                height: $(Selector.wrapper).height()
            })
        }

        // Plugin Definition
        // =================
        function Plugin(option) {
            return this.each(function () {
                var $this = $(this)
                var data = $this.data(DataKey)

                if (!data) {
                    var options = $.extend({}, Default, $this.data(), typeof option == 'object' && option)
                    $this.data(DataKey, (data = new ControlSidebar($this, options)))
                }

                if (typeof option == 'string') data.toggle()
            })
        }

        var old = $.fn.controlSidebar

        $.fn.controlSidebar = Plugin
        $.fn.controlSidebar.Constructor = ControlSidebar

        // No Conflict Mode
        // ================
        $.fn.controlSidebar.noConflict = function () {
            $.fn.controlSidebar = old
            return this
        }

        // ControlSidebar Data API
        // =======================
        $(document).on('click', Selector.data, function (event) {
            if (event) event.preventDefault()
            Plugin.call($(this), 'toggle')
        })

    }(jQuery)


    /* Layout()
     * ========
     * Implements AdminLTE layout.
     * Fixes the layout height in case min-height fails.
     *
     * @usage activated automatically upon window load.
     *        Configure any options by passing data-option="value"
     *        to the body tag.
     */
    + function ($) {
        'use strict'

        var DataKey = 'lte.layout'

        var Default = {
            slimscroll: true,
            resetHeight: true
        }

        var Selector = {
            wrapper: '.wrapper',
            contentWrapper: '.content-wrapper',
            layoutBoxed: '.layout-boxed',
            mainFooter: '.main-footer',
            mainHeader: '.main-header',
            sidebar: '.sidebar',
            controlSidebar: '.control-sidebar',
            fixed: '.fixed',
            sidebarMenu: '.sidebar-menu',
            logo: '.main-header .logo'
        }

        var ClassName = {
            fixed: 'fixed',
            holdTransition: 'hold-transition'
        }

        var Layout = function (options) {
            this.options = options
            this.bindedResize = false
            this.activate()
        }

        Layout.prototype.activate = function () {
            this.fix()
            this.fixSidebar()

            $('body').removeClass(ClassName.holdTransition)

            if (this.options.resetHeight) {
                $('body, html, ' + Selector.wrapper).css({
                    'height': 'auto',
                    'min-height': '100%'
                })
            }

            if (!this.bindedResize) {
                $(window).resize(function () {
                    this.fix()
                    this.fixSidebar()

                    $(Selector.logo + ', ' + Selector.sidebar).one('webkitTransitionEnd otransitionend oTransitionEnd msTransitionEnd transitionend', function () {
                        this.fix()
                        this.fixSidebar()
                    }.bind(this))
                }.bind(this))

                this.bindedResize = true
            }

            $(Selector.sidebarMenu).on('expanded.tree', function () {
                this.fix()
                this.fixSidebar()
            }.bind(this))

            $(Selector.sidebarMenu).on('collapsed.tree', function () {
                this.fix()
                this.fixSidebar()
            }.bind(this))
        }

        Layout.prototype.fix = function () {
            // Remove overflow from .wrapper if layout-boxed exists
            $(Selector.layoutBoxed + ' > ' + Selector.wrapper).css('overflow', 'hidden')

            // Get window height and the wrapper height
            var footerHeight = $(Selector.mainFooter).outerHeight() || 0
            var neg = $(Selector.mainHeader).outerHeight() + footerHeight
            var windowHeight = $(window).height()
            var sidebarHeight = $(Selector.sidebar).height() || 0

            // Set the min-height of the content and sidebar based on
            // the height of the document.
            if ($('body').hasClass(ClassName.fixed)) {
                $(Selector.contentWrapper).css('min-height', windowHeight - footerHeight)
            } else {
                var postSetHeight

                if (windowHeight >= sidebarHeight) {
                    $(Selector.contentWrapper).css('min-height', windowHeight - neg)
                    postSetHeight = windowHeight - neg
                } else {
                    $(Selector.contentWrapper).css('min-height', sidebarHeight)
                    postSetHeight = sidebarHeight
                }

                // Fix for the control sidebar height
                var $controlSidebar = $(Selector.controlSidebar)
                if (typeof $controlSidebar !== 'undefined') {
                    if ($controlSidebar.height() > postSetHeight)
                        $(Selector.contentWrapper).css('min-height', $controlSidebar.height())
                }
            }
        }

        Layout.prototype.fixSidebar = function () {
            // Make sure the body tag has the .fixed class
            if (!$('body').hasClass(ClassName.fixed)) {
                if (typeof $.fn.slimScroll !== 'undefined') {
                    $(Selector.sidebar).slimScroll({ destroy: true }).height('auto')
                }
                return
            }

            // Enable slimscroll for fixed layout
            if (this.options.slimscroll) {
                if (typeof $.fn.slimScroll !== 'undefined') {
                    // Destroy if it exists
                    $(Selector.sidebar).slimScroll({ destroy: true }).height('auto')

                    // Add slimscroll
                    $(Selector.sidebar).slimScroll({
                        height: ($(window).height() - $(Selector.mainHeader).height()) + 'px',
                        color: 'rgba(0,0,0,0.2)',
                        size: '3px'
                    })
                }
            }
        }

        // Plugin Definition
        // =================
        function Plugin(option) {
            return this.each(function () {
                var $this = $(this)
                var data = $this.data(DataKey)

                if (!data) {
                    var options = $.extend({}, Default, $this.data(), typeof option === 'object' && option)
                    $this.data(DataKey, (data = new Layout(options)))
                }

                if (typeof option === 'string') {
                    if (typeof data[option] === 'undefined') {
                        throw new Error('No method named ' + option)
                    }
                    data[option]()
                }
            })
        }

        var old = $.fn.layout

        $.fn.layout = Plugin
        $.fn.layout.Constuctor = Layout

        // No conflict mode
        // ================
        $.fn.layout.noConflict = function () {
            $.fn.layout = old
            return this
        }

        // Layout DATA-API
        // ===============
        $(window).on('load', function () {
            Plugin.call($('body'))
        })
    }(jQuery)


    /* PushMenu()
     * ==========
     * Adds the push menu functionality to the sidebar.
     *
     * @usage: $('.btn').pushMenu(options)
     *          or add [data-toggle="push-menu"] to any button
     *          Pass any option as data-option="value"
     */
    + function ($) {
        'use strict'

        var DataKey = 'lte.pushmenu'

        var Default = {
            collapseScreenSize: 767,
            expandOnHover: false,
            expandTransitionDelay: 200
        }

        var Selector = {
            collapsed: '.sidebar-collapse',
            open: '.sidebar-open',
            mainSidebar: '.main-sidebar',
            contentWrapper: '.content-wrapper',
            searchInput: '.sidebar-form .form-control',
            button: '[data-toggle="push-menu"]',
            mini: '.sidebar-mini',
            expanded: '.sidebar-expanded-on-hover',
            layoutFixed: '.fixed'
        }

        var ClassName = {
            collapsed: 'sidebar-collapse',
            open: 'sidebar-open',
            mini: 'sidebar-mini',
            expanded: 'sidebar-expanded-on-hover',
            expandFeature: 'sidebar-mini-expand-feature',
            layoutFixed: 'fixed'
        }

        var Event = {
            expanded: 'expanded.pushMenu',
            collapsed: 'collapsed.pushMenu'
        }

        // PushMenu Class Definition
        // =========================
        var PushMenu = function (options) {
            this.options = options
            this.init()
        }

        PushMenu.prototype.init = function () {
            //Load left side menu state
            var tmpLeftSidebarState = get('leftSidebarState')

            if (tmpLeftSidebarState == 'closed') {
                console.log('left side bar is closed')
                $('body').addClass('sidebar-collapse')
                console.log('class removed')
            }

            if (this.options.expandOnHover
                || ($('body').is(Selector.mini + Selector.layoutFixed))) {
                this.expandOnHover()
                $('body').addClass(ClassName.expandFeature)
            }

            $(Selector.contentWrapper).click(function () {
                // Enable hide menu when clicking on the content-wrapper on small screens
                if ($(window).width() <= this.options.collapseScreenSize && $('body').hasClass(ClassName.open)) {
                    this.close()
                }
            }.bind(this))

            // __Fix for android devices
            $(Selector.searchInput).click(function (e) {
                e.stopPropagation()
            })
        }

        PushMenu.prototype.toggle = function () {
            var windowWidth = $(window).width()
            var isOpen = !$('body').hasClass(ClassName.collapsed)

            if (windowWidth <= this.options.collapseScreenSize) {
                isOpen = $('body').hasClass(ClassName.open)
            }

            if (!isOpen) {
                this.open()
                store('leftSidebarState', 'opened')
                console.log('saved as open')
            } else {
                this.close()
                store('leftSidebarState', 'closed')
                console.log('saved as closed')
            }
        }

        PushMenu.prototype.open = function () {
            var windowWidth = $(window).width()

            if (windowWidth > this.options.collapseScreenSize) {
                $('body').removeClass(ClassName.collapsed)
                    .trigger($.Event(Event.expanded))
            }
            else {
                $('body').addClass(ClassName.open)
                    .trigger($.Event(Event.expanded))
            }
        }

        PushMenu.prototype.close = function () {
            var windowWidth = $(window).width()
            if (windowWidth > this.options.collapseScreenSize) {
                $('body').addClass(ClassName.collapsed)
                    .trigger($.Event(Event.collapsed))
            } else {
                $('body').removeClass(ClassName.open + ' ' + ClassName.collapsed)
                    .trigger($.Event(Event.collapsed))
            }
        }

        PushMenu.prototype.expandOnHover = function () {
            $(Selector.mainSidebar).hover(function () {
                if ($('body').is(Selector.mini + Selector.collapsed)
                    && $(window).width() > this.options.collapseScreenSize) {
                    this.expand()
                }
            }.bind(this), function () {
                if ($('body').is(Selector.expanded)) {
                    this.collapse()
                }
            }.bind(this))
        }

        PushMenu.prototype.expand = function () {
            setTimeout(function () {
                $('body').removeClass(ClassName.collapsed)
                    .addClass(ClassName.expanded)
            }, this.options.expandTransitionDelay)
        }

        PushMenu.prototype.collapse = function () {
            setTimeout(function () {
                $('body').removeClass(ClassName.expanded)
                    .addClass(ClassName.collapsed)
            }, this.options.expandTransitionDelay)
        }

        // PushMenu Plugin Definition
        // ==========================
        function Plugin(option) {
            return this.each(function () {
                var $this = $(this)
                var data = $this.data(DataKey)

                if (!data) {
                    var options = $.extend({}, Default, $this.data(), typeof option == 'object' && option)
                    $this.data(DataKey, (data = new PushMenu(options)))
                }

                if (option == 'toggle') data.toggle()
            })
        }

        var old = $.fn.pushMenu

        $.fn.pushMenu = Plugin
        $.fn.pushMenu.Constructor = PushMenu

        // No Conflict Mode
        // ================
        $.fn.pushMenu.noConflict = function () {
            $.fn.pushMenu = old
            return this
        }

        // Data API
        // ========
        $(document).on('click', Selector.button, function (e) {
            e.preventDefault()
            Plugin.call($(this), 'toggle')
        })
        $(window).on('load', function () {
            Plugin.call($(Selector.button))
        })
    }(jQuery)

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
        //if ($('body').hasClass('control-sidebar-open')) {
        //  $('[data-controlsidebar="control-sidebar-open"]').attr('checked', 'checked')
        //}

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

    // Create the menu
    var $layoutSettings = $('<div />')

    // Layout options
    $layoutSettings.append(
        '<h4 class="control-sidebar-heading">'
        + 'Layout Options'
        + '</h4>'
        // Fixed layout
        + '<div class="form-group">'
        + '<label class="control-sidebar-subheading">'
        + '<input type="checkbox"data-layout="fixed"class="pull-right"/> '
        + 'Fixed layout'
        + '</label>'
        + '<p>Activate the fixed layout. You can\'t use fixed and boxed layouts together</p>'
        + '</div>'
        // Boxed layout
        + '<div class="form-group">'
        + '<label class="control-sidebar-subheading">'
        + '<input type="checkbox"data-layout="layout-boxed" class="pull-right"/> '
        + 'Boxed Layout'
        + '</label>'
        + '<p>Activate the boxed layout</p>'
        + '</div>'
        // Control Sidebar Toggle
        + '<div class="form-group">'
        + '<label class="control-sidebar-subheading">'
        + '<input type="checkbox"data-controlsidebar="control-sidebar-open"class="pull-right"/> '
        + 'Toggle Right Sidebar Slide'
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

    $tabPane.append($layoutSettings)
    $('#control-sidebar-info-tab').after($tabPane)

    setup()

    $('[data-toggle="tooltip"]').tooltip()
})