<template>
    <div id="app">
        <div class="head">
            <a href="#" class="logo">
                <img src="./assets/logo.png" alt="asf logo">
            </a>
            <h1 class="text-center" v-html="$t('app.name')"></h1>
        </div>
        <div class="menu">
            <ul>
                <li><router-link :to="{ name: 'home' }" active-class="active" v-html="$t('link.home')" exact></router-link></li>
                <li><router-link :to="{ name: 'asf-config' }" active-class="active" v-html="$t('link.asf')"></router-link></li>
                <li><router-link :to="{ name: 'bot-config' }" active-class="active" v-html="$t('link.bot')"></router-link></li>
            </ul>
        </div>

        <router-view></router-view>
    </div>
</template>

<script>
    export default {
        name: 'app',
        data() {
            return {}
        }
    }
</script>

<style lang="scss">
    @import 'assets/kube-6.5.2/css/kube.css';

    #wrapper {
        font-family: 'Open Sans', Helvetica, Arial, sans-serif;
        margin: auto;
        max-width: 1128px;
        display: flex;
        flex-direction: column;
        padding: 35px;
    }

    .head {
        display: flex;
        flex-direction: column;
        margin-bottom: 10px;
    }

    .logo {
        img {
            height: 128px;
        }
    }

    .menu {
        display: flex;
        font-size: 1.3rem;
        font-weight: bold;
        margin-bottom: 20px;

        ul {
            margin: 0 auto;
            list-style: none;
        }

        li {
            display: inline-block;

            a {
                display: block;
                padding: 5px 10px;
            }
        }
    }

    a {
        text-decoration: none;
        color: #255bbd;

        &:hover, &.active {
            color: #f03c69;
        }
    }

    .logo {
        margin: 0 auto;
    }
</style>
