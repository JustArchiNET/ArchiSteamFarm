<template>
    <div class="form-item">
        <label :for="schema.field">
            {{ schema.label }}
            <span v-if="schema.required" class="req">*</span>
            <span v-if="schema.description" class="desc">{{ $t(schema.description) }}</span>
        </label>

        <div class="row gutters">
            <div class="col col-10">
                <div class="form-input">
                    <input v-if="!schema.values" type="text" :name="schema.field" :placeholder="schema.placeholder" :id="schema.field" class="set-value" :class="{ error: invalid }"
                           v-model="setValue">
                    <span v-if="!schema.values && invalid" class="error">{{ errors.join(' ') }}</span>
                    <select v-if="schema.values" v-model="setValue" :id="schema.field">
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
            <span v-for="(item, index) in items" class="label outline" @click.prevent="removeElement(index)">{{ resolveOption(item, schema.values) }}</span>
        </p>
    </div>
</template>

<script>
  import { each } from 'lodash';
  import Input from '../mixin/Input.vue';

  export default {
    mixins: [Input],
    name: 'InputSet',
    computed: {
      errors() {
        return this.schema.values ? [] : this.validate(this.setValue);
      },
      invalid() {
        return this.errors.length !== 0;
      }
    },
    data() {
      return {
        items: [], // Vue doesn't work well with Sets...
        setValue: this.schema.defaultValue
      };
    },
    methods: {
      addElement() {
        if (!this.setValue && this.setValue !== 0) return;
        if (this.hasErrors()) return;
        if (!this.items.includes(this.setValue)) this.items.push(this.setValue);
        this.setValue = this.schema.defaultValue;
        this.$emit('update', this.items, this.schema.field);
      },
      removeElement(index) {
        this.items.splice(index, 1);
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
        if (!this.invalid) return false;

        const fields = [];
        each(this.$el.getElementsByClassName('set-value'), field => fields.push(field));

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
