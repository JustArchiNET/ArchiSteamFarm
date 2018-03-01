import { isArray, isNil, isNumber, isString } from 'lodash';

function checkEmpty(value, required) {
    if (isNil(value) || value === '') {
        if (required) return ['Field required!'];
        else return [];
    }

    return null;
}

function limitedNumber(min, max) {
    return function(value, schema) {
        const emptyError = checkEmpty(value, schema.required);
        if (!isNil(emptyError)) return emptyError;

        const err = [];

        value = parseInt(value, 10);

        if (!isNumber(value) || isNaN(value)) {
            err.push('Not a valid number!');
        } else {
            if (value > max) err.push('Value too big!');
            else if (value < min) err.push('Value too small!');
        }

        return err;
    };
}

function limitedString(min, max) {
    return function(value, schema) {
        const emptyError = checkEmpty(value, schema.required);
        if (!isNil(emptyError)) return emptyError;

        const err = [];

        if (!isString(value)) {
            err.push('Not a valid string!');
        } else {
            if (value.length > max) err.push('Text too long!');
            else if (value.length < min) err.push('Text too short!');
        }

        return err;
    };
}

export default {
    required(value, schema) {
        const emptyError = checkEmpty(value, schema.required);
        if (!isNil(emptyError)) return emptyError;
        return [];
    },
    string(value, schema) {
        const emptyError = checkEmpty(value, schema.required);
        if (!isNil(emptyError)) return emptyError;

        const err = [];

        if (!isString(value)) err.push('This is not a text!');

        return err;
    },
    steamid(value, schema) {
        const emptyError = checkEmpty(value, schema.required);
        if (!isNil(emptyError)) return emptyError;

        const err = [];

        const re = /^[1-9][0-9]{16}$/;
        if (!re.test(value)) err.push('This is not a valid steamid!');

        return err;
    },
    masterClan(value, schema) {
        const emptyError = checkEmpty(value, schema.required);
        if (!isNil(emptyError)) return emptyError;

        const err = [];

        const re = /^[1-9][0-9]{17}$/;
        if (!re.test(value)) err.push('This is not a valid clan id!');

        return err;
    },
    parentalPIN(value, schema) {
        const emptyError = checkEmpty(value, schema.required);
        if (!isNil(emptyError)) return emptyError;

        const err = [];

        if (!isString(value)) {
            err.push('Not a valid string!');
        } else {
            if (value.length > 4) err.push('Text too long!');
            else if (value.length < 4) err.push('Text too short!');
        }

        value = parseInt(value, 10);

        if (!isNumber(value) || isNaN(value)) {
            err.push('Not a valid number!');
        }

        return err;
    },
    tradeToken: limitedString(8, 8),
    byte: limitedNumber(0, 255),
    ushort: limitedNumber(0, 65535),
    uint: limitedNumber(0, 4294967295)
};
