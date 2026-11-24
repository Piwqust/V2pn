// Errors of the API: a stable code the site keys on, an HTTP status and a short Russian message for the person.

const TABLE = {
  bad_request: [400, 'Запрос не понят.'],
  bad_json: [400, 'В запросе не JSON.'],
  invalid: [422, 'Проверьте поля.'],
  too_large: [413, 'Слишком большой запрос.'],
  csrf: [403, 'Запрос пришёл не с сайта V2pn.'],
  unauthorized: [401, 'Войдите заново.'],
  forbidden: [403, 'Нет доступа.'],
  not_found: [404, 'Не найдено.'],
  method: [405, 'Так нельзя.'],
  rate_limited: [429, 'Слишком часто. Попробуйте чуть позже.'],
  password_invalid: [400, 'Логин или пароль не подходят.'],
  login_taken: [409, 'Такой логин уже занят.'],
  conflict: [409, 'Состояние изменилось. Обновите страницу.'],
  domain_taken: [409, 'Этот домен уже у другого партнёра.'],
  domain_unproved: [422, 'Сначала подтвердите, что домен ваш.'],
  state: [409, 'Сейчас это сделать нельзя.'],
  check_failed: [422, 'Ссылка на подписку не прошла проверку. Исправьте и сохраните снова.'],
  check_busy: [503, 'Сервер занят проверками. Попробуйте через минуту.'],
  look_invalid: [422, 'Пока нельзя опубликовать: исправьте отмеченное.'],
  publish_failed: [500, 'Не удалось опубликовать. Модераторы уже видят ошибку.'],
  internal: [500, 'Что-то сломалось на сервере. Попробуйте позже.'],
};

export class HubError extends Error {
  constructor(code, { message, fields, status, detail } = {}) {
    const [defaultStatus, defaultMessage] = TABLE[code] || TABLE.internal;
    super(message || defaultMessage);
    this.code = TABLE[code] ? code : 'internal';
    this.status = status || defaultStatus;
    this.fields = fields;
    this.detail = detail;
  }

  toJSON() {
    const error = { code: this.code, message: this.message };
    if (this.fields && Object.keys(this.fields).length) error.fields = this.fields;
    if (this.detail !== undefined) error.detail = this.detail;
    return { error };
  }
}

/** Throws "invalid" with <fields> ({field: Russian message}) when any is set. */
export function assertFields(fields) {
  if (Object.keys(fields).length) throw new HubError('invalid', { fields });
}
