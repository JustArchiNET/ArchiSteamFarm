<template>
    <form method="post" action="" class="form" id="asf-form" onsubmit="return false;">
        <div class="row align-center" v-if="versions.length > 1">
            <div class="col col-2">
                <div class="form-input">
                    <select v-model="selectedVersion" id="version">
                        <option v-for="version in versions" :value="version">{{ version }}</option>
                    </select>
                </div>
            </div>
        </div>

        <fieldset v-for="group in schema" v-if="!group.advanced || displayAdvanced">
            <legend>{{ group.legend }}</legend>
            <component v-for="inputSchema in group.fields" :is="inputSchema.type" :schema="inputSchema" :key="inputSchema.field" v-if="!inputSchema.advanced || displayAdvanced"
                       @update="updateModel"></component>
        </fieldset>

        <div class="form-item">
            <button @click.prevent="downloadJSON" class="button">Download</button>
            <button @click.prevent="toggleAdvanced" class="button secondary" :class="{ outline: !displayAdvanced }">Toggle advanced</button>
        </div>
    </form>
</template>

<script>
    import { each } from "lodash";
    import Config from "./mixin/Config.vue";
    import Schema from "../schema";

    export default {
        name: 'BotConfig',
        mixins: [ Config ],
        data() {
            return {
                versions: [ 'latest' ]
            }
        },
        computed: {
            schema() {
                if (Schema[this.selectedVersion]) {
                    return Schema[this.selectedVersion].bot;
                }

                return Schema.latest.bot;
            }
        },
        methods: {
            downloadJSON() {
                if (!this.validateForm()) return;

                const json = this.processModelToJSON(this.model);
                const text = JSON.stringify(json);
                const filename = `${this.model.name}.json`;

                this.downloadText(text, filename);
            },
            processModelToJSON(model) {
                if (model.GamesPlayedWhileIdle && model.GamesPlayedWhileIdle.length) {
                    model.GamesPlayedWhileIdle = model.GamesPlayedWhileIdle.map(value => parseInt(value, 10)).filter(value => !isNaN(value) && value > 0);
                }

                each(model, (value, key) => {
                    if (typeof value === 'string' && value === "") delete model[key];
                });

                return model;
            }
        }
    }
</script>
