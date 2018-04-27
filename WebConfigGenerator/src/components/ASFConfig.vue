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
            <legend>{{ $t(group.legend) }}</legend>
            <component v-for="inputSchema in group.fields" :is="inputSchema.type" :schema="inputSchema" :key="inputSchema.field" v-if="!inputSchema.advanced || displayAdvanced"
                       @update="updateModel"></component>
        </fieldset>

        <div class="form-item">
            <button @click.prevent="downloadJSON" class="button">{{ $t('button.download') }}</button>
            <button @click.prevent="toggleAdvanced" class="button secondary" :class="{ outline: !displayAdvanced }">{{ $t('button.advanced') }}</button>
        </div>
    </form>
</template>

<script>
  import { each } from 'lodash';
  import Config from './mixin/Config.vue';

  export default {
    name: 'ASFConfig',
    mixins: [Config],
    data() { return { type: 'asf', filename: 'ASF.json' }; },
    methods: {
      processModelToJSON(model) {
        if (model.Blacklist && model.Blacklist.length) {
          model.Blacklist = model.Blacklist.map(item => parseInt(item, 10)).filter(item => !isNaN(item) && item > 0);
        }

        each(model, (value, key) => {
          if (typeof value === 'string' && value === '') delete model[key];
        });

        return model;
      }
    }
  };
</script>
