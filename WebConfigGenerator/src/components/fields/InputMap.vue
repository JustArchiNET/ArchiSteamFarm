<template>
    <div class="form-item">
        <label :for="schema.field">
            {{ schema.label }}
            <span v-if="schema.required" class="req">*</span>
            <span v-if="schema.description" class="desc">{{ $t(schema.description) }}</span>
        </label>

        <div class="row gutters">
            <div class="col col-5">
                <div class="form-item">
                    <input v-if="!schema.keys" type="text" :placeholder="schema.keyPlaceholder" class="map-key" :class="{ error: keyInvalid }" v-model="mapKey">
                    <span v-if="!schema.keys && keyInvalid" class="error">{{ keyErrors.join(' ') }}</span>
                    <select v-if="schema.keys" v-model="mapKey">
                        <option v-for="key in schema.keys" :value="key.value">{{ $t(key.name) }}</option>
                    </select>
                </div>
            </div>
            <div class="col col-5">
                <div class="form-item">
                    <input v-if="!schema.values" type="text" :placeholder="schema.valuePlaceholder" class="map-value" :class="{ error: valueInvalid }" v-model="mapValue">
                    <span v-if="!schema.values && valueInvalid" class="error">{{ valueErrors.join(' ') }}</span>
                    <select v-if="schema.values" v-model="mapValue">
                        <option v-for="val in schema.values" :value="val.value">{{ $t(val.name) }}</option>
                    </select>
                </div>
            </div>
            <div class="col col-2">
                <div class="form-input">
                    <button class="button outline w100" @click.prevent="addElement">{{ $t('static.add') }}</button>
                </div>
            </div>
        </div>

        <p class="label-list">
            <span v-for="(value, key) in items" class="label outline" @click.prevent="removeElement(key)">{{ resolveOption(key, schema.keys)
                }} => {{ resolveOption(value, schema.values) }}</span>
        </p>
    </div>
</template>

<script>
  import { each } from 'lodash';
  import Input from '../mixin/Input.vue';

  export default {
    mixins: [Input],
    name: 'InputMap',
    computed: {
      keyErrors() {
        if (!this.schema.keyValidator) return [];
        return this.validate(this.mapKey, this.schema.keyValidator);
      },
      keyInvalid() {
        return this.keyErrors.length !== 0;
      },
      valueErrors() {
        if (!this.schema.valueValidator) return [];
        return this.validate(this.mapValue, this.schema.valueValidator);
      },
      valueInvalid() {
        return this.valueErrors.length !== 0;
      }
    },
    data() {
      return {
        items: {}, // Vue doesn't work well with Maps...
        mapKey: this.schema.defaultKey,
        mapValue: this.schema.defaultValue
      };
    },
    methods: {
      addElement() {
        if (!this.mapValue && this.mapValue !== 0 || !this.mapKey && this.mapKey !== 0) return;

        if (this.hasErrors()) return;

        this.items[this.mapKey] = this.mapValue;
        this.mapValue = this.schema.defaultValue;
        this.mapKey = this.schema.defaultKey;
        this.$emit('update', this.items, this.schema.field);
      },
      removeElement(key) {
        this.$delete(this.items, key);
        this.$emit('update', this.items, this.schema.field);
      },
      resolveOption(toResolve, options) {
        if (!options) return toResolve;

        options.forEach(({ value, name }) => {
          if (toResolve === value) toResolve = name;
        });

        return toResolve;
      },
      hasErrors() {
        const invalid = this.keyInvalid || this.valueInvalid;
        if (!invalid) return false;

        const fields = [];
        if (this.keyInvalid) each(this.$el.getElementsByClassName('map-key'), field => fields.push(field));
        if (this.valueInvalid) each(this.$el.getElementsByClassName('map-value'), field => fields.push(field));

        clearTimeout(this.shakeTimeout);
        each(fields, field => { field.classList.add('shake'); });
        this.shakeTimeout = setTimeout(() => { each(fields, field => { field.classList.remove('shake'); }); }, 500);

        return true;
      }
    }
  };
</script>

<style lang="scss">
    .label-list {
        margin-top: 5px;
        margin-bottom: 0;

        .label {
            margin: 0 5px;
            cursor: pointer;
            transition: all 0.1s;

            &:hover {
                background: black;
                color: white;
            }
        }
    }
</style>
