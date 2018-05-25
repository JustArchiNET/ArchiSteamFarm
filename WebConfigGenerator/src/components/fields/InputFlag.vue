<template>
    <div class="form-item">
        <label :for="schema.field">
            {{ schema.label }}
            <span v-if="schema.required" class="req">*</span>
            <span v-if="schema.description" class="desc">{{ schema.description }}</span>
        </label>

        <div class="row gutters">
            <div class="col col-10">
                <div class="form-input">
                    <select v-model="flagValue" :id="schema.field">
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
  import Input from '../mixin/Input.vue';

  export default {
    mixins: [Input],
    name: 'InputFlag',
    data() {
      return {
        items: [], // Vue doesn't work well with Sets...
        flagValue: this.schema.defaultValue
      };
    },
    methods: {
      addElement() {
        if (!this.flagValue && this.flagValue !== 0) return;
        if (!this.items.includes(this.flagValue)) this.items.push(this.flagValue);
        this.flagValue = this.schema.defaultValue;
        this.value = this.items.reduce((el, sum) => { return el + sum; });
      },
      removeElement(index) {
        this.items.splice(index, 1);
        this.value = this.items.reduce((el, sum) => { return el + sum; });
      },
      resolveOption(toResolve, options) {
        if (!options) return toResolve;

        options.forEach(({ value, name }) => {
          if (toResolve === value) toResolve = name;
        });

        return toResolve;
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
